using ClosedXML.Excel;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Tests;

public sealed class DemoWorkbookRegressionTests
{
    [Fact]
    public void FirstThreeSheets_ReproduceDailyAndMonthlyNewWages()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "2026年7月份计件表7.31.xlsx"));
        Assert.True(File.Exists(sourcePath));
        using var workbook = new XLWorkbook(sourcePath); var allKeys = new HashSet<string>(StringComparer.Ordinal); var materialNames = new HashSet<string>(StringComparer.Ordinal); var lineCount = 0;
        var sheets = workbook.Worksheets.Take(3).ToList();
        Assert.Equal(3, sheets.Count);
        foreach (var sheet in sheets)
        {
            var dateColumns = DateColumns(sheet); var monthly = 0m;
            foreach (var dateColumn in dateColumns)
            {
                var dailyWage = 0m; var dailyAttainment = 0m;
                for (var row = 3; row < 1958; row++)
                {
                    var material = sheet.Cell(row, 1).GetString().Trim(); var specification = sheet.Cell(row, 2).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(material) || string.IsNullOrWhiteSpace(specification)) continue;
                    if (!sheet.Cell(row, dateColumn).TryGetValue<decimal>(out var quantity) || quantity <= 0) continue;
                    var target = sheet.Cell(row, 3).GetValue<decimal>(); var buckle = sheet.Cell(row, 5).GetValue<decimal>(); var buckleRate = sheet.Cell(row, 6).GetValue<decimal>();
                    var wage = quantity * buckle * buckleRate; var attainment = quantity * buckle / target;
                    Assert.InRange(Math.Abs(wage - sheet.Cell(row, dateColumn + 2).GetValue<decimal>()), 0m, 0.0000000001m);
                    Assert.InRange(Math.Abs(attainment - sheet.Cell(row, dateColumn + 3).GetValue<decimal>()), 0m, 0.0000000001m);
                    dailyWage += wage; dailyAttainment += attainment; monthly += wage; lineCount++; materialNames.Add(material); allKeys.Add($"{material}\u001f{specification}\u001f{buckle}");
                }
                Assert.True(dailyWage >= 0); Assert.True(dailyAttainment >= 0);
            }
            Assert.True(PricingMath.RoundMoney(monthly) > 0);
        }
        Assert.Equal(27, DateColumns(sheets[0]).Count); Assert.Equal(456, lineCount); Assert.Equal(15, materialNames.Count); Assert.Equal(262, allKeys.Count);
    }

    private static List<int> DateColumns(IXLWorksheet sheet)
    {
        var result = new List<int>();
        for (var column = 7; column <= 111; column += 4)
        {
            if (!sheet.Cell(1, column).TryGetValue<DateTime>(out _) && !sheet.Cell(1, column).TryGetValue<double>(out _)) break;
            result.Add(column);
        }
        return result;
    }
}
