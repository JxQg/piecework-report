using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Web.Services;

public sealed record ImportPreviewRow(
    int RowNumber,
    DateTime WorkDate,
    string EmployeeCode,
    string EmployeeName,
    string MachineCode,
    string MachineName,
    string SpecificationCode,
    string MaterialName,
    string Specification,
    decimal Quantity,
    string? Note,
    int WagePeriodId,
    int EmployeeId,
    int MachineId,
    int MaterialId,
    int MaterialSpecificationId);

public sealed class ImportPreviewResult
{
    public List<ImportPreviewRow> Rows { get; } = [];
    public List<string> Errors { get; } = [];
    public bool IsValid => Rows.Count > 0 && Errors.Count == 0;
}

public sealed record SpecificationImportPreviewRow(
    int RowNumber,
    string? MaterialCode,
    string MaterialName,
    string Description,
    decimal BuckleCount,
    IReadOnlyList<string> MachineCodes,
    string? Note,
    int? ExistingMaterialId);

public sealed class SpecificationImportPreviewResult
{
    public List<SpecificationImportPreviewRow> Rows { get; } = [];
    public List<string> Errors { get; } = [];
    public bool IsValid => Rows.Count > 0 && Errors.Count == 0;
}

public sealed record EmployeeImportPreviewRow(
    int RowNumber,
    string Code,
    string Name,
    bool IsActive,
    string Action,
    int? ExistingEmployeeId);

public sealed class EmployeeImportPreviewResult
{
    public List<EmployeeImportPreviewRow> Rows { get; } = [];
    public List<string> Errors { get; } = [];
    public bool IsValid => Rows.Count > 0 && Errors.Count == 0;
}

public sealed record ExportPackage(byte[] Content, PeriodWageReport Report);

public sealed class ExcelService(
    AppDbContext db,
    WageCalculationService calculationService,
    DatabaseBackupService backupService,
    CodeGenerationService codeGenerationService,
    ExportInvalidationService invalidationService)
{
    private static readonly string[] ProductionHeaders = ["日期", "员工编码", "员工姓名", "机器编码", "机器名称", "规格编码", "完成件数", "备注"];
    private static readonly string[] SpecificationHeaders = ["物料编码", "物料名称", "规格描述", "扣数", "可加工机器编码", "备注"];
    private static readonly string[] EmployeeHeaders = ["员工编码", "姓名", "状态"];
    private static readonly char[] MachineSeparators = [',', '，', '、', ';', '；'];
    private static readonly XLColor HeaderGreen = XLColor.FromHtml("#C9E4B4");

    public async Task<byte[]> CreateImportTemplateAsync()
    {
        var employees = await db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        var links = await db.MachineSpecifications.AsNoTracking().Where(x => x.IsActive && x.Machine.IsActive && x.MaterialSpecification.IsActive)
            .Include(x => x.Machine).Include(x => x.MaterialSpecification).ThenInclude(x => x.Material)
            .OrderBy(x => x.Machine.Code).ThenBy(x => x.MaterialSpecification.Code).ToListAsync();
        using var workbook = new XLWorkbook();
        var input = workbook.AddWorksheet("计件导入");
        WriteHeaders(input, ProductionHeaders);
        input.SheetView.FreezeRows(1);
        input.Column(1).Style.DateFormat.Format = "yyyy-mm-dd";
        input.Column(7).Style.NumberFormat.Format = "#,##0.###";
        var employeeSheet = workbook.AddWorksheet("员工参考");
        WriteHeaders(employeeSheet, ["员工编码", "员工姓名"]);
        for (var i = 0; i < employees.Count; i++) { employeeSheet.Cell(i + 2, 1).Value = employees[i].Code; employeeSheet.Cell(i + 2, 2).Value = employees[i].Name; }
        var specificationSheet = workbook.AddWorksheet("机器规格参考");
        WriteHeaders(specificationSheet, ["机器编码", "机器名称", "规格编码", "物料名称", "规格描述", "扣数"]);
        for (var i = 0; i < links.Count; i++)
        {
            specificationSheet.Cell(i + 2, 1).Value = links[i].Machine.Code; specificationSheet.Cell(i + 2, 2).Value = links[i].Machine.Name;
            specificationSheet.Cell(i + 2, 3).Value = links[i].MaterialSpecification.Code; specificationSheet.Cell(i + 2, 4).Value = links[i].MaterialSpecification.Material.Name;
            specificationSheet.Cell(i + 2, 5).Value = links[i].MaterialSpecification.Description; specificationSheet.Cell(i + 2, 6).Value = links[i].MaterialSpecification.BuckleCount;
        }
        FinalizeReferenceWorkbook(workbook);
        return Save(workbook);
    }

    public async Task<byte[]> CreateSpecificationTemplateAsync()
    {
        var materials = await db.Materials.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        var machines = await db.Machines.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code).ToListAsync();
        using var workbook = new XLWorkbook();
        var input = workbook.AddWorksheet("规格导入");
        WriteHeaders(input, SpecificationHeaders);
        input.SheetView.FreezeRows(1);
        input.Column(4).Style.NumberFormat.Format = "#,##0.###";
        var materialSheet = workbook.AddWorksheet("物料参考"); WriteHeaders(materialSheet, ["物料编码", "物料名称"]);
        for (var i = 0; i < materials.Count; i++) { materialSheet.Cell(i + 2, 1).Value = materials[i].Code; materialSheet.Cell(i + 2, 2).Value = materials[i].Name; }
        var machineSheet = workbook.AddWorksheet("机器参考"); WriteHeaders(machineSheet, ["机器编码", "机器名称"]);
        for (var i = 0; i < machines.Count; i++) { machineSheet.Cell(i + 2, 1).Value = machines[i].Code; machineSheet.Cell(i + 2, 2).Value = machines[i].Name; }
        var instructions = workbook.AddWorksheet("填写说明");
        instructions.Cell("A1").Value = "向已有物料追加规格时填写物料编码；批量创建新物料时物料编码留空，并填写唯一、明确的物料名称。";
        instructions.Cell("A2").Value = "多个机器编码使用逗号分隔。文件存在重复规格、非法扣数、未知机器或物料名称歧义时，整批拒绝。";
        instructions.Column(1).Width = 90; instructions.Rows(1, 2).Style.Alignment.WrapText = true;
        FinalizeReferenceWorkbook(workbook);
        return Save(workbook);
    }

    public Task<byte[]> CreateEmployeeTemplateAsync()
    {
        using var workbook = new XLWorkbook();
        var input = workbook.AddWorksheet("员工导入");
        WriteHeaders(input, EmployeeHeaders);
        input.SheetView.FreezeRows(1);
        var instructions = workbook.AddWorksheet("填写说明");
        instructions.Cell("A1").Value = "员工编码和姓名必填；状态填写“在用”或“停用”，留空时默认为“在用”。";
        instructions.Cell("A2").Value = "系统按员工编码新增或更新。文件内编码重复或任一行无效时，整批数据不会写入。";
        instructions.Column(1).Width = 90;
        instructions.Rows(1, 2).Style.Alignment.WrapText = true;
        FinalizeReferenceWorkbook(workbook);
        return Task.FromResult(Save(workbook));
    }

    public async Task<ImportPreviewResult> PreviewImportAsync(string path)
    {
        var result = new ImportPreviewResult();
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        if (!ValidateHeaders(sheet, ProductionHeaders, result.Errors)) return result;
        var employees = await db.Employees.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var machines = await db.Machines.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var specifications = await db.MaterialSpecifications.AsNoTracking().Where(x => x.IsActive && x.Material.IsActive).Include(x => x.Material).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var periods = await db.WagePeriods.AsNoTracking().Select(x => new { x.Id, x.Year, x.Month }).ToListAsync();
        var workdayKeys = (await db.WagePeriodWorkdays.AsNoTracking().Select(x => new { x.WagePeriodId, x.WorkDate }).ToListAsync()).Select(x => $"{x.WagePeriodId}:{x.WorkDate:yyyyMMdd}").ToHashSet();
        var links = await db.MachineSpecifications.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.MachineId, x.MaterialSpecificationId }).ToListAsync();
        var rules = await db.PricingRules.AsNoTracking().Where(x => x.MachineSpecificationId != null)
            .Select(x => new { x.Id, x.WagePeriodId, MachineSpecificationId = x.MachineSpecificationId!.Value, x.DefaultTargetBuckleCount, IsValid = x.Mode == PricingMode.AttainmentBased ? x.TargetDailyWage > 0 : x.DirectPieceRate > 0 }).ToListAsync();
        var overrides = await db.EmployeePricingOverrides.AsNoTracking().Select(x => new { x.PricingRuleId, x.EmployeeId, x.TargetBuckleCount }).ToListAsync();
        var existingKeys = (await db.ProductionRecords.AsNoTracking().Where(x => x.MaterialSpecificationId != null).Select(x => new { x.WorkDate, x.EmployeeId, x.MachineId, SpecificationId = x.MaterialSpecificationId!.Value }).ToListAsync())
            .Select(x => RecordKey(x.WorkDate, x.EmployeeId, x.MachineId, x.SpecificationId)).ToHashSet(StringComparer.Ordinal);
        var fileKeys = new HashSet<string>(StringComparer.Ordinal);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber); if (row.Cells(1, ProductionHeaders.Length).All(x => x.IsEmpty())) continue;
            if (!TryReadDate(row.Cell(1), out var workDate)) { result.Errors.Add($"第 {rowNumber} 行日期无效。"); continue; }
            var employeeCode = row.Cell(2).GetString().Trim(); var machineCode = row.Cell(4).GetString().Trim(); var specificationCode = row.Cell(6).GetString().Trim();
            if (!employees.TryGetValue(employeeCode, out var employee)) { result.Errors.Add($"第 {rowNumber} 行员工编码“{employeeCode}”不存在或已停用。"); continue; }
            if (!machines.TryGetValue(machineCode, out var machine)) { result.Errors.Add($"第 {rowNumber} 行机器编码“{machineCode}”不存在或已停用。"); continue; }
            if (!specifications.TryGetValue(specificationCode, out var specification)) { result.Errors.Add($"第 {rowNumber} 行规格编码“{specificationCode}”不存在或已停用。"); continue; }
            if (!row.Cell(7).TryGetValue<decimal>(out var quantity) || quantity <= 0) { result.Errors.Add($"第 {rowNumber} 行完成件数必须大于 0。"); continue; }
            var period = periods.SingleOrDefault(x => x.Year == workDate.Year && x.Month == workDate.Month);
            if (period is null) { result.Errors.Add($"第 {rowNumber} 行日期所在月份尚未创建。"); continue; }
            if (!workdayKeys.Contains($"{period.Id}:{workDate:yyyyMMdd}")) { result.Errors.Add($"第 {rowNumber} 行日期不是该月配置工作日。"); continue; }
            var link = links.SingleOrDefault(x => x.MachineId == machine.Id && x.MaterialSpecificationId == specification.Id);
            if (link is null) { result.Errors.Add($"第 {rowNumber} 行机器不能加工该规格。"); continue; }
            var rule = rules.SingleOrDefault(x => x.WagePeriodId == period.Id && x.MachineSpecificationId == link.Id);
            if (rule is null || !rule.IsValid) { result.Errors.Add($"第 {rowNumber} 行机器规格尚未完成本月可用计价配置，请联系经理。"); continue; }
            var target = overrides.SingleOrDefault(x => x.PricingRuleId == rule.Id && x.EmployeeId == employee.Id)?.TargetBuckleCount ?? rule.DefaultTargetBuckleCount ?? 0;
            if (target <= 0) { result.Errors.Add($"第 {rowNumber} 行员工尚未配置有效达标数，请联系经理。"); continue; }
            var key = RecordKey(workDate, employee.Id, machine.Id, specification.Id);
            if (!fileKeys.Add(key)) { result.Errors.Add($"第 {rowNumber} 行与导入文件中的其他记录重复。"); continue; }
            if (existingKeys.Contains(key)) { result.Errors.Add($"第 {rowNumber} 行在系统中已存在，请先修改或删除原记录。"); continue; }
            result.Rows.Add(new ImportPreviewRow(rowNumber, workDate, employee.Code, employee.Name, machine.Code, machine.Name, specification.Code, specification.Material.Name, specification.Description, quantity, NullIfWhiteSpace(row.Cell(8).GetString()), period.Id, employee.Id, machine.Id, specification.MaterialId, specification.Id));
        }
        if (result.Rows.Count == 0 && result.Errors.Count == 0) result.Errors.Add("文件中没有可导入的计件记录。");
        return result;
    }

    public async Task<int> CommitImportAsync(string path, string username)
    {
        var preview = await PreviewImportAsync(path);
        if (!preview.IsValid) throw new InvalidOperationException("导入文件已发生变化或包含无效数据，请重新预览。");
        await backupService.CreateBackupAsync("beforeimport");
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.ProductionRecords.AddRange(preview.Rows.Select(x => new ProductionRecord { WagePeriodId = x.WagePeriodId, WorkDate = x.WorkDate, EmployeeId = x.EmployeeId, MachineId = x.MachineId, MaterialId = x.MaterialId, MaterialSpecificationId = x.MaterialSpecificationId, Quantity = x.Quantity, Note = x.Note, Source = "Excel", UpdatedBy = username }));
        await invalidationService.MarkPeriodsAsync(preview.Rows.Select(x => x.WagePeriodId));
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return preview.Rows.Count;
    }

    public async Task<EmployeeImportPreviewResult> PreviewEmployeeImportAsync(string path)
    {
        var result = new EmployeeImportPreviewResult();
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        if (!ValidateHeaders(sheet, EmployeeHeaders, result.Errors)) return result;
        var existing = await db.Employees.AsNoTracking().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var fileCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            if (row.Cells(1, EmployeeHeaders.Length).All(x => x.IsEmpty())) continue;
            var code = row.Cell(1).GetString().Trim().ToUpperInvariant();
            var name = row.Cell(2).GetString().Trim();
            var status = row.Cell(3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(code)) { result.Errors.Add($"第 {rowNumber} 行员工编码不能为空。"); continue; }
            if (code.Length > 30) { result.Errors.Add($"第 {rowNumber} 行员工编码不能超过 30 个字符。"); continue; }
            if (string.IsNullOrWhiteSpace(name)) { result.Errors.Add($"第 {rowNumber} 行姓名不能为空。"); continue; }
            if (name.Length > 80) { result.Errors.Add($"第 {rowNumber} 行姓名不能超过 80 个字符。"); continue; }
            if (!fileCodes.Add(code)) { result.Errors.Add($"第 {rowNumber} 行员工编码“{code}”在文件中重复。"); continue; }
            bool isActive;
            if (string.IsNullOrWhiteSpace(status) || status == "在用") isActive = true;
            else if (status == "停用") isActive = false;
            else { result.Errors.Add($"第 {rowNumber} 行状态只能填写“在用”或“停用”。"); continue; }
            existing.TryGetValue(code, out var employee);
            var action = employee is null ? "新增" : employee.Name == name && employee.IsActive == isActive ? "无变化" : "更新";
            result.Rows.Add(new EmployeeImportPreviewRow(rowNumber, code, name, isActive, action, employee?.Id));
        }
        if (result.Rows.Count == 0 && result.Errors.Count == 0) result.Errors.Add("文件中没有可导入的员工记录。");
        return result;
    }

    public async Task<int> CommitEmployeeImportAsync(string path)
    {
        var preview = await PreviewEmployeeImportAsync(path);
        if (!preview.IsValid) throw new InvalidOperationException("导入文件已发生变化或包含无效数据，请重新预览。");
        await backupService.CreateBackupAsync("beforeemployeeimport");
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var row in preview.Rows)
        {
            if (row.ExistingEmployeeId.HasValue)
            {
                var employee = await db.Employees.SingleAsync(x => x.Id == row.ExistingEmployeeId.Value);
                employee.Name = row.Name;
                employee.IsActive = row.IsActive;
            }
            else
            {
                db.Employees.Add(new Employee { Code = row.Code, Name = row.Name, IsActive = row.IsActive });
            }
        }
        await db.SaveChangesAsync();
        await invalidationService.MarkAllPeriodsAsync();
        await transaction.CommitAsync();
        return preview.Rows.Count;
    }

    public async Task<SpecificationImportPreviewResult> PreviewSpecificationImportAsync(string path)
    {
        var result = new SpecificationImportPreviewResult();
        using var workbook = new XLWorkbook(path); var sheet = workbook.Worksheet(1);
        if (!ValidateHeaders(sheet, SpecificationHeaders, result.Errors)) return result;
        var materials = await db.Materials.AsNoTracking().ToListAsync();
        var machines = await db.Machines.AsNoTracking().Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var existingSpecs = await db.MaterialSpecifications.AsNoTracking().Select(x => new { x.MaterialId, x.Description }).ToListAsync();
        var fileKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber); if (row.Cells(1, SpecificationHeaders.Length).All(x => x.IsEmpty())) continue;
            var materialCode = NullIfWhiteSpace(row.Cell(1).GetString()); var materialName = row.Cell(2).GetString().Trim(); var description = row.Cell(3).GetString().Trim();
            if (string.IsNullOrWhiteSpace(materialName)) { result.Errors.Add($"第 {rowNumber} 行物料名称不能为空。"); continue; }
            if (string.IsNullOrWhiteSpace(description)) { result.Errors.Add($"第 {rowNumber} 行规格描述不能为空。"); continue; }
            if (!row.Cell(4).TryGetValue<decimal>(out var buckleCount) || buckleCount <= 0) { result.Errors.Add($"第 {rowNumber} 行扣数必须大于 0。"); continue; }
            Material? material = null;
            if (materialCode is not null)
            {
                material = materials.SingleOrDefault(x => string.Equals(x.Code, materialCode, StringComparison.OrdinalIgnoreCase));
                if (material is null) { result.Errors.Add($"第 {rowNumber} 行物料编码“{materialCode}”不存在。"); continue; }
                if (!string.Equals(material.Name, materialName, StringComparison.Ordinal)) { result.Errors.Add($"第 {rowNumber} 行物料编码与名称不匹配。"); continue; }
            }
            else
            {
                var matches = materials.Where(x => string.Equals(x.Name, materialName, StringComparison.Ordinal)).ToList();
                if (matches.Count > 1) { result.Errors.Add($"第 {rowNumber} 行物料名称“{materialName}”存在歧义，请填写编码。"); continue; }
                material = matches.SingleOrDefault();
            }
            if (material is not null && existingSpecs.Any(x => x.MaterialId == material.Id && string.Equals(x.Description, description, StringComparison.OrdinalIgnoreCase))) { result.Errors.Add($"第 {rowNumber} 行规格已存在。"); continue; }
            var machineCodes = row.Cell(5).GetString().Split(MachineSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (machineCodes.Count == 0) { result.Errors.Add($"第 {rowNumber} 行至少填写一台可加工机器。"); continue; }
            var unknown = machineCodes.FirstOrDefault(x => !machines.ContainsKey(x)); if (unknown is not null) { result.Errors.Add($"第 {rowNumber} 行机器编码“{unknown}”不存在或已停用。"); continue; }
            var materialKey = material?.Id.ToString(CultureInfo.InvariantCulture) ?? $"NEW:{materialName}"; var key = $"{materialKey}|{description}|{buckleCount}";
            if (!fileKeys.Add(key)) { result.Errors.Add($"第 {rowNumber} 行与导入文件中的其他规格重复。"); continue; }
            result.Rows.Add(new SpecificationImportPreviewRow(rowNumber, material?.Code, materialName, description, buckleCount, machineCodes, NullIfWhiteSpace(row.Cell(6).GetString()), material?.Id));
        }
        if (result.Rows.Count == 0 && result.Errors.Count == 0) result.Errors.Add("文件中没有可导入的规格记录。");
        return result;
    }

    public async Task<int> CommitSpecificationImportAsync(string path)
    {
        var preview = await PreviewSpecificationImportAsync(path);
        if (!preview.IsValid) throw new InvalidOperationException("导入文件已发生变化或包含无效数据，请重新预览。");
        await backupService.CreateBackupAsync("beforespecimport");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var machines = await db.Machines.Where(x => x.IsActive).ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var createdMaterials = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var row in preview.Rows)
        {
            Material material;
            if (row.ExistingMaterialId.HasValue) material = await db.Materials.SingleAsync(x => x.Id == row.ExistingMaterialId.Value);
            else if (!createdMaterials.TryGetValue(row.MaterialName, out material!))
            {
                material = new Material { Code = await codeGenerationService.NextMaterialCodeAsync(), Name = row.MaterialName, IsActive = true, LegacySpecification = "-", LegacyBuckleCount = 0 };
                db.Materials.Add(material); await db.SaveChangesAsync(); createdMaterials.Add(row.MaterialName, material);
            }
            var specification = new MaterialSpecification { Code = await codeGenerationService.NextSpecificationCodeAsync(material), MaterialId = material.Id, Description = row.Description, BuckleCount = row.BuckleCount, Note = row.Note, IsActive = true };
            db.MaterialSpecifications.Add(specification); await db.SaveChangesAsync();
            db.MachineSpecifications.AddRange(row.MachineCodes.Select(code => new MachineSpecification { MachineId = machines[code].Id, MaterialSpecificationId = specification.Id, IsActive = true }));
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return preview.Rows.Count;
    }

    public async Task<ExportPackage> CreateReportAsync(int periodId)
    {
        var report = await calculationService.CalculateAsync(periodId) ?? throw new InvalidOperationException("工资月份不存在。");
        if (report.Issues.Count > 0) throw new InvalidOperationException("存在未配置或无效的计价规则，不能导出工资表。");
        var workdays = await db.WagePeriodWorkdays.AsNoTracking().Where(x => x.WagePeriodId == periodId).OrderBy(x => x.WorkDate).Select(x => x.WorkDate).ToListAsync();
        if (workdays.Count == 0) throw new InvalidOperationException("该工资月份尚未配置实际工作日。");
        var adjustments = await db.PayAdjustments.AsNoTracking().Where(x => x.WagePeriodId == periodId).Include(x => x.Employee).OrderBy(x => x.AdjustmentDate).ToListAsync();
        var employeeIds = report.Lines.Select(x => x.EmployeeId).Union(adjustments.Select(x => x.EmployeeId)).Distinct().ToList();
        if (employeeIds.Count == 0) throw new InvalidOperationException("该工资月份没有可导出的员工数据。");
        var employees = await db.Employees.AsNoTracking().Where(x => employeeIds.Contains(x.Id)).OrderBy(x => x.Code).ToListAsync();
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees)
        {
            AddEmployeeSheet(workbook, UniqueSheetName(employee.Name, usedNames), employee, workdays, report, adjustments.Where(x => x.EmployeeId == employee.Id).ToList());
        }
        workbook.RecalculateAllFormulas();
        return new ExportPackage(Save(workbook), report);
    }

    private static void AddEmployeeSheet(XLWorkbook workbook, string sheetName, Employee employee, IReadOnlyList<DateTime> workdays, PeriodWageReport report, IReadOnlyList<PayAdjustment> adjustments)
    {
        var sheet = workbook.AddWorksheet(sheetName); sheet.ShowGridLines = false;
        var lines = report.Lines.Where(x => x.EmployeeId == employee.Id).ToList();
        var groups = lines.GroupBy(x => new { x.MachineCode, x.MachineName, x.MaterialCode, x.MaterialName, x.SpecificationCode, x.Specification })
            .OrderBy(x => x.Key.MachineCode).ThenBy(x => x.Key.MaterialCode).ThenBy(x => x.Key.SpecificationCode).ToList();
        var firstDataRow = 3; var dataRowCount = Math.Max(1, groups.Count); var lastDataRow = firstDataRow + dataRowCount - 1;
        var fixedHeaders = new[] { "机器", "物料", "规格", "员工达标扣数", "规格扣数", "每扣单价" };
        for (var i = 0; i < fixedHeaders.Length; i++) { sheet.Range(1, i + 1, 2, i + 1).Merge(); sheet.Cell(1, i + 1).Value = fixedHeaders[i]; }
        var dayStart = fixedHeaders.Length + 1;
        for (var dayIndex = 0; dayIndex < workdays.Count; dayIndex++)
        {
            var column = dayStart + dayIndex * 3; sheet.Range(1, column, 1, column + 2).Merge(); sheet.Cell(1, column).Value = workdays[dayIndex]; sheet.Cell(1, column).Style.DateFormat.Format = "m月d日";
            sheet.Cell(2, column).Value = "完成件数"; sheet.Cell(2, column + 1).Value = "计件工资"; sheet.Cell(2, column + 2).Value = "达标率";
        }
        var summaryStart = dayStart + workdays.Count * 3;
        var summaryHeaders = new[] { "月计件工资", "增项合计", "月工资合计" };
        for (var i = 0; i < summaryHeaders.Length; i++) { sheet.Range(1, summaryStart + i, 2, summaryStart + i).Merge(); sheet.Cell(1, summaryStart + i).Value = summaryHeaders[i]; }
        var groupIndex = 0;
        foreach (var group in groups)
        {
            var row = firstDataRow + groupIndex++; var exemplar = group.First();
            sheet.Cell(row, 1).Value = $"{exemplar.MachineCode} {exemplar.MachineName}"; sheet.Cell(row, 2).Value = $"{exemplar.MaterialCode} {exemplar.MaterialName}"; sheet.Cell(row, 3).Value = $"{exemplar.SpecificationCode} {exemplar.Specification}";
            sheet.Cell(row, 4).Value = exemplar.TargetBuckleCount; sheet.Cell(row, 5).Value = exemplar.BuckleCount; sheet.Cell(row, 6).Value = exemplar.BuckleRate;
            for (var dayIndex = 0; dayIndex < workdays.Count; dayIndex++)
            {
                var dayLines = group.Where(x => x.WorkDate == workdays[dayIndex]).ToList(); var column = dayStart + dayIndex * 3;
                if (dayLines.Count == 0) continue;
                sheet.Cell(row, column).Value = dayLines.Sum(x => x.Quantity); sheet.Cell(row, column + 1).Value = dayLines.Sum(x => x.Wage); sheet.Cell(row, column + 2).Value = dayLines.Sum(x => x.AttainmentRate);
            }
        }
        var attainmentRow = lastDataRow + 1; var pieceworkRow = lastDataRow + 2; var adjustmentRow = lastDataRow + 3; var totalRow = lastDataRow + 4;
        foreach (var pair in new[] { (attainmentRow, "日达标率"), (pieceworkRow, "日计件工资"), (adjustmentRow, "工资增项"), (totalRow, "日工资合计") }) { sheet.Range(pair.Item1, 1, pair.Item1, 6).Merge(); sheet.Cell(pair.Item1, 1).Value = pair.Item2; }
        for (var dayIndex = 0; dayIndex < workdays.Count; dayIndex++)
        {
            var column = dayStart + dayIndex * 3; var wageColumn = column + 1; var attainmentColumn = column + 2;
            sheet.Cell(attainmentRow, attainmentColumn).FormulaA1 = $"SUM({sheet.Cell(firstDataRow, attainmentColumn).Address}:{sheet.Cell(lastDataRow, attainmentColumn).Address})";
            sheet.Cell(pieceworkRow, wageColumn).FormulaA1 = $"SUM({sheet.Cell(firstDataRow, wageColumn).Address}:{sheet.Cell(lastDataRow, wageColumn).Address})";
            sheet.Cell(adjustmentRow, wageColumn).Value = adjustments.Where(x => x.AdjustmentDate.Date == workdays[dayIndex]).Sum(x => x.Amount);
            sheet.Cell(totalRow, wageColumn).FormulaA1 = $"{sheet.Cell(pieceworkRow, wageColumn).Address}+{sheet.Cell(adjustmentRow, wageColumn).Address}";
        }
        var wageRanges = Enumerable.Range(0, workdays.Count).Select(i => $"{sheet.Cell(firstDataRow, dayStart + i * 3 + 1).Address}:{sheet.Cell(lastDataRow, dayStart + i * 3 + 1).Address}");
        sheet.Range(firstDataRow, summaryStart, lastDataRow, summaryStart).Merge(); sheet.Cell(firstDataRow, summaryStart).FormulaA1 = $"ROUND(SUM({string.Join(',', wageRanges)}),2)";
        sheet.Range(firstDataRow, summaryStart + 1, lastDataRow, summaryStart + 1).Merge(); sheet.Cell(firstDataRow, summaryStart + 1).Value = adjustments.Sum(x => x.Amount);
        sheet.Range(firstDataRow, summaryStart + 2, lastDataRow, summaryStart + 2).Merge(); sheet.Cell(firstDataRow, summaryStart + 2).FormulaA1 = $"{sheet.Cell(firstDataRow, summaryStart).Address}+{sheet.Cell(firstDataRow, summaryStart + 1).Address}";
        var adjustmentTitleRow = totalRow + 2; sheet.Range(adjustmentTitleRow, 1, adjustmentTitleRow, 6).Merge(); sheet.Cell(adjustmentTitleRow, 1).Value = "工资增项明细";
        var adjustmentHeaderRow = adjustmentTitleRow + 1; var adjustmentHeaders = new[] { "日期", "类别", "金额", "说明" };
        for (var i = 0; i < adjustmentHeaders.Length; i++) sheet.Cell(adjustmentHeaderRow, i + 1).Value = adjustmentHeaders[i];
        var rowNumber = adjustmentHeaderRow + 1;
        foreach (var item in adjustments) { sheet.Cell(rowNumber, 1).Value = item.AdjustmentDate; sheet.Cell(rowNumber, 2).Value = item.Category; sheet.Cell(rowNumber, 3).Value = item.Amount; sheet.Cell(rowNumber, 4).Value = item.Note ?? string.Empty; rowNumber++; }
        if (adjustments.Count == 0) { sheet.Range(rowNumber, 1, rowNumber, 4).Merge(); sheet.Cell(rowNumber, 1).Value = "无工资增项"; }
        var lastUsedRow = Math.Max(rowNumber, adjustmentHeaderRow); var lastColumn = summaryStart + 2;
        var used = sheet.Range(1, 1, lastUsedRow, lastColumn); used.Style.Font.FontName = "宋体"; used.Style.Font.FontSize = 11; used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center; used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(1, 1, 2, lastColumn).Style.Fill.BackgroundColor = HeaderGreen; sheet.Range(1, 1, 2, lastColumn).Style.Font.Bold = true; sheet.Range(1, 1, 2, lastColumn).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(attainmentRow, 1, totalRow, lastColumn).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F7EE"); sheet.Range(attainmentRow, 1, totalRow, 6).Style.Font.Bold = true;
        sheet.Range(adjustmentTitleRow, 1, adjustmentTitleRow, 6).Style.Fill.BackgroundColor = HeaderGreen; sheet.Range(adjustmentTitleRow, 1, adjustmentTitleRow, 6).Style.Font.Bold = true;
        sheet.Range(adjustmentHeaderRow, 1, adjustmentHeaderRow, 4).Style.Fill.BackgroundColor = HeaderGreen; sheet.Range(adjustmentHeaderRow, 1, adjustmentHeaderRow, 4).Style.Font.Bold = true;
        sheet.Range(firstDataRow, 4, lastDataRow, 5).Style.NumberFormat.Format = "#,##0.###"; sheet.Range(firstDataRow, 6, lastDataRow, 6).Style.NumberFormat.Format = "0.000000";
        for (var dayIndex = 0; dayIndex < workdays.Count; dayIndex++) { var column = dayStart + dayIndex * 3; sheet.Range(firstDataRow, column, totalRow, column).Style.NumberFormat.Format = "#,##0.###"; sheet.Range(firstDataRow, column + 1, totalRow, column + 1).Style.NumberFormat.Format = "#,##0.00"; sheet.Range(firstDataRow, column + 2, totalRow, column + 2).Style.NumberFormat.Format = "0.00%"; sheet.Columns(column, column + 2).Width = 11; }
        sheet.Range(firstDataRow, summaryStart, lastDataRow, summaryStart + 2).Style.NumberFormat.Format = "#,##0.00"; sheet.Range(adjustmentHeaderRow + 1, 3, lastUsedRow, 3).Style.NumberFormat.Format = "#,##0.00"; sheet.Column(1).Width = 20; sheet.Column(2).Width = 22; sheet.Column(3).Width = 27; sheet.Columns(4, 6).Width = 14; sheet.Columns(summaryStart, summaryStart + 2).Width = 15; sheet.Rows(1, 2).Height = 24; sheet.SheetView.FreezeRows(2); sheet.SheetView.FreezeColumns(6);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape; sheet.PageSetup.FitToPages(1, 0);
    }

    private static string UniqueSheetName(string employeeName, ISet<string> used)
    {
        var cleaned = Regex.Replace(employeeName, "[\\\\/:*?\\[\\]]", "_").Trim(); if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "员工"; if (cleaned.Length > 31) cleaned = cleaned[..31];
        var candidate = cleaned; var suffix = 2;
        while (!used.Add(candidate)) { var tail = $" ({suffix++})"; candidate = cleaned[..Math.Min(cleaned.Length, 31 - tail.Length)] + tail; }
        return candidate;
    }

    private static bool ValidateHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers, ICollection<string> errors)
    {
        for (var column = 1; column <= headers.Count; column++) if (!string.Equals(sheet.Cell(1, column).GetString().Trim(), headers[column - 1], StringComparison.Ordinal)) errors.Add($"第 {column} 列表头应为“{headers[column - 1]}”。");
        return errors.Count == 0;
    }
    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<string> headers) { for (var i = 0; i < headers.Count; i++) sheet.Cell(1, i + 1).Value = headers[i]; var range = sheet.Range(1, 1, 1, headers.Count); range.Style.Fill.BackgroundColor = HeaderGreen; range.Style.Font.Bold = true; range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; range.Style.Border.InsideBorder = XLBorderStyleValues.Thin; }
    private static void FinalizeReferenceWorkbook(XLWorkbook workbook) { foreach (var sheet in workbook.Worksheets) { sheet.Style.Font.FontName = "宋体"; sheet.Style.Font.FontSize = 11; sheet.ColumnsUsed().AdjustToContents(); foreach (var column in sheet.ColumnsUsed()) if (column.Width > 42) column.Width = 42; } }
    private static byte[] Save(XLWorkbook workbook) { using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray(); }
    private static bool TryReadDate(IXLCell cell, out DateTime value) { if (cell.TryGetValue<DateTime>(out value)) { value = value.Date; return true; } if (DateTime.TryParse(cell.GetString(), out value)) { value = value.Date; return true; } return false; }
    private static string RecordKey(DateTime date, int employeeId, int machineId, int specificationId) => $"{date:yyyyMMdd}:{employeeId}:{machineId}:{specificationId}";
    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
