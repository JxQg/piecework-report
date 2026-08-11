using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Tests;

public sealed class ExcelServiceTests
{
    [Fact]
    public async Task EmployeeImport_AddsUpdatesAndRejectsDuplicateCodesAtomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = database.CreateExcelService();
        var path = Path.Combine(Path.GetTempPath(), $"employee-import-{Guid.NewGuid():N}.xlsx");
        try
        {
            await File.WriteAllBytesAsync(path, await service.CreateEmployeeTemplateAsync());
            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("员工导入");
                sheet.Cell("A2").Value = "e001"; sheet.Cell("B2").Value = "张三"; sheet.Cell("C2").Value = "在用";
                sheet.Cell("A3").Value = "E002"; sheet.Cell("B3").Value = "李四"; sheet.Cell("C3").Value = "停用";
                workbook.Save();
            }
            var preview = await service.PreviewEmployeeImportAsync(path);
            Assert.True(preview.IsValid); Assert.All(preview.Rows, row => Assert.Equal("新增", row.Action));
            Assert.Equal(2, await service.CommitEmployeeImportAsync(path));
            Assert.Equal(2, await database.Db.Employees.CountAsync());

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("员工导入"); sheet.Cell("B2").Value = "张三新"; sheet.Cell("C2").Value = "停用"; workbook.Save();
            }
            var update = await service.PreviewEmployeeImportAsync(path); Assert.True(update.IsValid); Assert.Contains(update.Rows, row => row.Code == "E001" && row.Action == "更新");
            await service.CommitEmployeeImportAsync(path);
            Assert.False((await database.Db.Employees.SingleAsync(x => x.Code == "E001")).IsActive);

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet("员工导入"); sheet.Cell("A3").Value = "E001"; workbook.Save();
            }
            var invalid = await service.PreviewEmployeeImportAsync(path); Assert.False(invalid.IsValid); Assert.Contains(invalid.Errors, x => x.Contains("重复"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitEmployeeImportAsync(path));
            Assert.Equal(2, await database.Db.Employees.CountAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ProductionTemplatePreviewCommitAndEmployeeReport_WorkAsOneWorkflow()
    {
        await using var database = await TestDatabase.CreateAsync(); var seed = await WageCalculationTests.SeedAsync(database.Db);
        database.Db.PricingRules.Add(WageCalculationTests.NewRule(seed, seed.AttainmentLink, PricingMode.AttainmentBased, 4m, 4m, null));
        database.Db.PayAdjustments.Add(new PayAdjustment { WagePeriodId = seed.Period.Id, EmployeeId = seed.Employee.Id, AdjustmentDate = new DateTime(2026, 7, 3), Category = "非工作日增项", Amount = 5m, UpdatedBy = "manager" });
        await database.Db.SaveChangesAsync(); var service = database.CreateExcelService();
        var template = await service.CreateImportTemplateAsync(); var path = Path.Combine(Path.GetTempPath(), $"piecework-import-{Guid.NewGuid():N}.xlsx");
        try
        {
            await File.WriteAllBytesAsync(path, template);
            using (var workbook = new XLWorkbook(path))
            {
                Assert.Equal(new[] { "计件导入", "员工参考", "机器规格参考" }, workbook.Worksheets.Select(x => x.Name));
                var input = workbook.Worksheet("计件导入");
                input.Cell("A2").Value = new DateTime(2026, 7, 1); input.Cell("B2").Value = seed.Employee.Code; input.Cell("C2").Value = seed.Employee.Name;
                input.Cell("D2").Value = seed.Machine.Code; input.Cell("E2").Value = seed.Machine.Name; input.Cell("F2").Value = seed.AttainmentSpecification.Code; input.Cell("G2").Value = 1m; input.Cell("H2").Value = "白班"; workbook.Save();
            }
            var preview = await service.PreviewImportAsync(path); Assert.True(preview.IsValid); Assert.Equal(seed.AttainmentSpecification.Code, Assert.Single(preview.Rows).SpecificationCode);
            Assert.Equal(1, await service.CommitImportAsync(path, "clerk"));
            var package = await service.CreateReportAsync(seed.Period.Id);
            using var report = new XLWorkbook(new MemoryStream(package.Content));
            var sheet = Assert.Single(report.Worksheets); Assert.Equal(seed.Employee.Name, sheet.Name);
            Assert.Equal("机器", sheet.Cell("A1").GetString()); Assert.Equal("完成件数", sheet.Cell("G2").GetString()); Assert.Equal("计件工资", sheet.Cell("H2").GetString()); Assert.Equal("达标率", sheet.Cell("I2").GetString());
            Assert.Equal("宋体", sheet.Cell("A1").Style.Font.FontName); Assert.Equal(XLColor.FromHtml("#C9E4B4"), sheet.Cell("A1").Style.Fill.BackgroundColor);
            Assert.Equal(2, sheet.SheetView.SplitRow); Assert.Equal(6, sheet.SheetView.SplitColumn);
            Assert.Equal("日达标率", sheet.Cell("A4").GetString()); Assert.Equal("日计件工资", sheet.Cell("A5").GetString()); Assert.Equal("工资增项明细", sheet.Cell("A9").GetString());
            Assert.Equal(new DateTime(2026, 7, 3), sheet.Cell("A11").GetDateTime()); Assert.Equal(5m, sheet.Cell("C11").GetValue<decimal>());
            Assert.True(sheet.Cell("M3").HasFormula); Assert.Equal(4m, sheet.Cell("M3").GetValue<decimal>()); Assert.Equal(9m, sheet.Cell("O3").GetValue<decimal>());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidProductionRow_PreventsTheWholeImport()
    {
        await using var database = await TestDatabase.CreateAsync(); var seed = await WageCalculationTests.SeedAsync(database.Db); database.Db.PricingRules.Add(WageCalculationTests.NewRule(seed, seed.AttainmentLink, PricingMode.AttainmentBased, 4m, 4m, null)); await database.Db.SaveChangesAsync();
        var service = database.CreateExcelService(); var path = Path.Combine(Path.GetTempPath(), $"piecework-invalid-{Guid.NewGuid():N}.xlsx");
        try
        {
            await File.WriteAllBytesAsync(path, await service.CreateImportTemplateAsync());
            using (var workbook = new XLWorkbook(path)) { var sheet = workbook.Worksheet(1); WriteProduction(sheet, 2, seed, seed.Employee.Code); WriteProduction(sheet, 3, seed, "UNKNOWN"); workbook.Save(); }
            var preview = await service.PreviewImportAsync(path); Assert.False(preview.IsValid); Assert.Contains(preview.Errors, x => x.Contains("UNKNOWN")); await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitImportAsync(path, "clerk")); Assert.Equal(0, await database.Db.ProductionRecords.CountAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SpecificationImport_CreatesMaterialCodesAndRollsBackDuplicates()
    {
        await using var database = await TestDatabase.CreateAsync(); database.Db.Machines.Add(new Machine { Code = "M0001", Name = "一号机" }); await database.Db.SaveChangesAsync(); var service = database.CreateExcelService();
        var path = Path.Combine(Path.GetTempPath(), $"spec-import-{Guid.NewGuid():N}.xlsx");
        try
        {
            await File.WriteAllBytesAsync(path, await service.CreateSpecificationTemplateAsync());
            using (var workbook = new XLWorkbook(path)) { var sheet = workbook.Worksheet(1); sheet.Cell("B2").Value = "新物料"; sheet.Cell("C2").Value = "非标规格A"; sheet.Cell("D2").Value = 8m; sheet.Cell("E2").Value = "M0001"; workbook.Save(); }
            var preview = await service.PreviewSpecificationImportAsync(path); Assert.True(preview.IsValid); Assert.Equal(1, await service.CommitSpecificationImportAsync(path));
            Assert.Equal("P000001", (await database.Db.Materials.SingleAsync()).Code); Assert.Equal("P000001-S0001", (await database.Db.MaterialSpecifications.SingleAsync()).Code);
            using (var workbook = new XLWorkbook(path)) { var sheet = workbook.Worksheet(1); sheet.Range("A2:F2").CopyTo(sheet.Cell("A3")); workbook.Save(); }
            var invalid = await service.PreviewSpecificationImportAsync(path); Assert.False(invalid.IsValid); Assert.Equal(1, await database.Db.MaterialSpecifications.CountAsync());
        }
        finally { File.Delete(path); }
    }

    private static void WriteProduction(IXLWorksheet sheet, int row, WageCalculationTests.SeedData seed, string employeeCode)
    {
        sheet.Cell(row, 1).Value = new DateTime(2026, 7, 1); sheet.Cell(row, 2).Value = employeeCode; sheet.Cell(row, 3).Value = seed.Employee.Name; sheet.Cell(row, 4).Value = seed.Machine.Code; sheet.Cell(row, 5).Value = seed.Machine.Name; sheet.Cell(row, 6).Value = seed.AttainmentSpecification.Code; sheet.Cell(row, 7).Value = 1m;
    }
}
