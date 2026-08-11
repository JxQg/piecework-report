using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

if (args.Length >= 3 && string.Equals(args[0], "--copy-db", StringComparison.OrdinalIgnoreCase))
{
    var sourceDatabase = Path.GetFullPath(args[1]);
    var targetDatabase = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(Path.GetDirectoryName(targetDatabase)!);
    SqliteConnection.ClearAllPools();
    if (File.Exists(targetDatabase)) File.Delete(targetDatabase);
    await using var source = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly");
    await using var target = new SqliteConnection($"Data Source={targetDatabase}");
    await source.OpenAsync(); await target.OpenAsync(); source.BackupDatabase(target);
    Console.WriteLine($"Copied database: {sourceDatabase} -> {targetDatabase}");
    return 0;
}

if (args.Length >= 2 && string.Equals(args[0], "--verify-db", StringComparison.OrdinalIgnoreCase))
{
    var verifyPath = Path.GetFullPath(args[1]);
    var verifyOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={verifyPath}").Options;
    await using var verifyDb = new AppDbContext(verifyOptions);
    Console.WriteLine($"Periods={await verifyDb.WagePeriods.CountAsync()}; Machines={await verifyDb.Machines.CountAsync()}; Materials={await verifyDb.Materials.CountAsync()}; Specifications={await verifyDb.MaterialSpecifications.CountAsync()}; Records={await verifyDb.ProductionRecords.CountAsync()}");
    foreach (var item in await verifyDb.WagePeriods.OrderBy(x => x.Year).ThenBy(x => x.Month).Select(x => new { x.Year, x.Month, x.PlannedWorkdays }).ToListAsync())
        Console.WriteLine($"Period={item.Year}-{item.Month:00}; Workdays={item.PlannedWorkdays}");
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: PieceworkReport.DemoBuilder <source.xlsx> <output-directory> [--force]");
    return 2;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
var databasePath = Path.Combine(outputDirectory, "piecework-report.db");
if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source workbook was not found.", sourcePath);
if (File.Exists(databasePath) && !args.Contains("--force", StringComparer.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Demo database already exists: {databasePath}. Pass --force to rebuild it.");

Directory.CreateDirectory(outputDirectory);
SqliteConnection.ClearAllPools();
if (File.Exists(databasePath)) File.Delete(databasePath);

const int demoSheetCount = 3;
var parsed = ParseWorkbook(sourcePath, demoSheetCount);
Require(parsed.Workdays.Count == 27, $"Expected 27 workdays, found {parsed.Workdays.Count}.");
Require(parsed.Lines.Count == 456, $"Expected 456 production lines, found {parsed.Lines.Count}.");
Require(parsed.Lines.Select(x => x.MaterialName).Distinct().Count() == 15, "Expected 15 material categories.");
Require(parsed.Lines.Select(x => x.SpecificationKey).Distinct().Count() == 262, "Expected 262 used specifications.");
var sourceTotals = parsed.Lines.GroupBy(x => x.EmployeeName, StringComparer.Ordinal)
    .ToDictionary(x => x.Key, x => PricingMath.RoundMoney(x.Sum(line => line.CalculatedWage)), StringComparer.Ordinal);
Require(sourceTotals.Count == demoSheetCount, $"Expected {demoSheetCount} demo employees, found {sourceTotals.Count}.");

var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={databasePath}").Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();

var hasher = new PasswordHasher<AppUser>();
var manager = new AppUser { Username = "manager", PasswordHash = string.Empty, SecurityStamp = Guid.NewGuid().ToString("N"), Role = UserRole.Manager };
manager.PasswordHash = hasher.HashPassword(manager, "Manager@123");
var clerk = new AppUser { Username = "clerk", PasswordHash = string.Empty, SecurityStamp = Guid.NewGuid().ToString("N"), Role = UserRole.Clerk };
clerk.PasswordHash = hasher.HashPassword(clerk, "Clerk@123");
db.Users.AddRange(manager, clerk);

var employees = sourceTotals.Keys.Select((name, index) => new Employee { Code = $"E{index + 1:000}", Name = name }).ToDictionary(x => x.Name, StringComparer.Ordinal);
db.Employees.AddRange(employees.Values);
var machine = new Machine { Code = "M0001", Name = "原表未记录机器" };
db.Machines.Add(machine);
var period = new WagePeriod { Year = 2026, Month = 7, Budget = 16_200m, PlannedWorkdays = parsed.Workdays.Count, PlannedHeadcount = 3, ExportOutdated = true };
db.WagePeriods.Add(period);
await db.SaveChangesAsync();
db.WagePeriodWorkdays.AddRange(parsed.Workdays.Select(x => new WagePeriodWorkday { WagePeriodId = period.Id, WorkDate = x }));

var materials = parsed.Lines.Select(x => x.MaterialName).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
    .Select((name, index) => new Material { Code = $"P{index + 1:000000}", Name = name, LegacySpecification = "-", LegacyBuckleCount = 0 })
    .ToDictionary(x => x.Name, StringComparer.Ordinal);
db.Materials.AddRange(materials.Values);
await db.SaveChangesAsync();

var specifications = new Dictionary<string, MaterialSpecification>(StringComparer.Ordinal);
foreach (var material in materials.Values)
{
    var sourceSpecifications = parsed.Lines.Where(x => x.MaterialName == material.Name).GroupBy(x => x.SpecificationKey).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
    var index = 1;
    foreach (var sourceSpecification in sourceSpecifications)
    {
        var line = sourceSpecification.First();
        var specification = new MaterialSpecification { Code = $"{material.Code}-S{index++:0000}", MaterialId = material.Id, Description = line.Specification, BuckleCount = line.BuckleCount, Note = "来自原表前三个 Sheet", IsActive = true };
        db.MaterialSpecifications.Add(specification); specifications.Add(sourceSpecification.Key, specification);
    }
}
await db.SaveChangesAsync();

var links = specifications.Values.ToDictionary(x => x.Id, x => new MachineSpecification { MachineId = machine.Id, MaterialSpecificationId = x.Id, IsActive = true, Note = "原表未记录机器" });
db.MachineSpecifications.AddRange(links.Values);
await db.SaveChangesAsync();

var rules = new Dictionary<int, PricingRule>();
foreach (var pair in specifications)
{
    var sourceLines = parsed.Lines.Where(x => x.SpecificationKey == pair.Key).ToList();
    var specification = pair.Value;
    var rule = new PricingRule
    {
        WagePeriodId = period.Id,
        MachineSpecificationId = links[specification.Id].Id,
        MachineId = machine.Id,
        MaterialId = specification.MaterialId,
        Mode = PricingMode.AttainmentBased,
        TargetDailyWage = sourceLines.First().TargetDailyWage,
        DefaultTargetBuckleCount = sourceLines.First().TargetBuckleCount,
        Note = "原表日计件工资（新）公式"
    };
    db.PricingRules.Add(rule); rules.Add(specification.Id, rule);
}
await db.SaveChangesAsync();

foreach (var employeeGroup in parsed.Lines.GroupBy(x => (x.EmployeeName, x.SpecificationKey)))
{
    var specification = specifications[employeeGroup.Key.SpecificationKey];
    var rule = rules[specification.Id];
    var employeeTarget = employeeGroup.Select(x => x.TargetBuckleCount).Distinct().Single();
    if (employeeTarget != rule.DefaultTargetBuckleCount)
        db.EmployeePricingOverrides.Add(new EmployeePricingOverride { PricingRuleId = rule.Id, EmployeeId = employees[employeeGroup.Key.EmployeeName].Id, TargetBuckleCount = employeeTarget });
}

db.ProductionRecords.AddRange(parsed.Lines.Select(line =>
{
    var specification = specifications[line.SpecificationKey];
    return new ProductionRecord
    {
        WagePeriodId = period.Id,
        WorkDate = line.WorkDate,
        EmployeeId = employees[line.EmployeeName].Id,
        MachineId = machine.Id,
        MaterialId = specification.MaterialId,
        MaterialSpecificationId = specification.Id,
        Quantity = line.Quantity,
        Source = "示例原表",
        UpdatedBy = "demo-builder"
    };
}));
db.CodeSequences.AddRange(
    new CodeSequence { Name = "Machine", NextValue = 2 },
    new CodeSequence { Name = "Material", NextValue = materials.Count + 1 });
foreach (var material in materials.Values)
    db.CodeSequences.Add(new CodeSequence { Name = $"Specification:{material.Id}", NextValue = specifications.Values.Count(x => x.MaterialId == material.Id) + 1 });
await db.SaveChangesAsync();

var report = await new WageCalculationService(db).CalculateAsync(period.Id) ?? throw new InvalidOperationException("Demo wage report could not be calculated.");
Require(report.Issues.Count == 0, string.Join(Environment.NewLine, report.Issues.Select(x => x.Message)));
Require(report.Lines.Count == 456, "Demo report did not retain all 456 production lines.");
foreach (var expected in sourceTotals)
{
    var actual = report.EmployeeRows.Single(x => x.EmployeeName == expected.Key).PieceworkWage;
    Require(actual == expected.Value, $"Database regression failed for {expected.Key}: expected {expected.Value}, calculated {actual}.");
}

Console.WriteLine($"Created demo database: {databasePath}");
Console.WriteLine($"Employees={employees.Count}; Materials={materials.Count}; Specifications={specifications.Count}; Workdays={parsed.Workdays.Count}; ProductionLines={parsed.Lines.Count}");
foreach (var employee in report.EmployeeRows) Console.WriteLine($"{employee.EmployeeName}={employee.PieceworkWage:F2}");
return 0;

static ParsedWorkbook ParseWorkbook(string path, int sheetCount)
{
    using var workbook = new XLWorkbook(path);
    var sheets = workbook.Worksheets.Take(sheetCount).ToList();
    Require(sheets.Count == sheetCount, $"Expected at least {sheetCount} worksheets, found {sheets.Count}.");
    var lines = new List<SourceLine>();
    var workdays = new SortedSet<DateTime>();
    foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index)))
    {
        var employeeName = $"Employee {index + 1:000}";
        var dateColumns = new List<(int Column, DateTime Date)>();
        for (var column = 7; column <= (sheet.LastColumnUsed()?.ColumnNumber() ?? 6); column += 4)
        {
            if (!TryDate(sheet.Cell(1, column), out var date)) break;
            dateColumns.Add((column, date)); workdays.Add(date);
        }
        for (var row = 3; row <= (sheet.LastRowUsed()?.RowNumber() ?? 2); row++)
        {
            var material = sheet.Cell(row, 1).GetString().Trim(); var specification = sheet.Cell(row, 2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(material) || string.IsNullOrWhiteSpace(specification)) continue;
            if (!sheet.Cell(row, 3).TryGetValue<decimal>(out var target) || target <= 0) continue;
            if (!sheet.Cell(row, 5).TryGetValue<decimal>(out var buckle) || buckle <= 0) continue;
            if (!sheet.Cell(row, 6).TryGetValue<decimal>(out var buckleRate) || buckleRate <= 0) continue;
            foreach (var dateColumn in dateColumns)
            {
                if (!sheet.Cell(row, dateColumn.Column).TryGetValue<decimal>(out var quantity) || quantity <= 0) continue;
                lines.Add(new SourceLine(employeeName, material, specification, buckle, target, buckleRate, dateColumn.Date, quantity));
            }
        }
    }
    return new ParsedWorkbook(workdays.ToList(), lines);
}

static bool TryDate(IXLCell cell, out DateTime date)
{
    if (cell.TryGetValue<DateTime>(out date)) { date = date.Date; return true; }
    if (cell.TryGetValue<double>(out var serial)) { date = DateTime.FromOADate(serial).Date; return true; }
    date = default; return false;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record ParsedWorkbook(IReadOnlyList<DateTime> Workdays, IReadOnlyList<SourceLine> Lines);
internal sealed record SourceLine(string EmployeeName, string MaterialName, string Specification, decimal BuckleCount, decimal TargetBuckleCount, decimal BuckleRate, DateTime WorkDate, decimal Quantity)
{
    public string SpecificationKey => $"{MaterialName}\u001f{Specification}\u001f{BuckleCount}";
    public decimal TargetDailyWage => BuckleRate * TargetBuckleCount;
    public decimal CalculatedWage => Quantity * BuckleCount * BuckleRate;
}
