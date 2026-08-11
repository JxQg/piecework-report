using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;
using PieceworkReport.Web.Services;

namespace PieceworkReport.Tests;

public sealed class WageCalculationTests
{
    [Fact]
    public async Task CalculateAsync_KeepsDailyPrecisionAndRoundsMonthlyOnce()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seed = await SeedAsync(database.Db);
        database.Db.PricingRules.AddRange(
            NewRule(seed, seed.AttainmentLink, PricingMode.AttainmentBased, 4m, 4m, null),
            NewRule(seed, seed.DirectLink, PricingMode.DirectPieceRate, null, 2m, 0.335m));
        database.Db.ProductionRecords.AddRange(
            NewRecord(seed, seed.AttainmentSpecification, new DateTime(2026, 7, 1), 1m),
            NewRecord(seed, seed.DirectSpecification, new DateTime(2026, 7, 1), 1m),
            NewRecord(seed, seed.DirectSpecification, new DateTime(2026, 7, 2), 1m));
        database.Db.PayAdjustments.Add(new PayAdjustment { WagePeriodId = seed.Period.Id, EmployeeId = seed.Employee.Id, AdjustmentDate = new DateTime(2026, 7, 2), Category = "设备调试", Amount = 5.25m, UpdatedBy = "manager" });
        await database.Db.SaveChangesAsync();

        var report = await new WageCalculationService(database.Db).CalculateAsync(seed.Period.Id);

        Assert.NotNull(report); Assert.Empty(report.Issues);
        Assert.Collection(report.DailyRows,
            first => { Assert.Equal(4.335m, first.PieceworkWage); Assert.Equal(2m, first.AttainmentRate); },
            second => Assert.Equal(0.335m, second.PieceworkWage));
        Assert.Equal(4.67m, report.PieceworkTotal);
        Assert.Equal(5.25m, report.AdjustmentTotal);
        Assert.Equal(9.92m, report.FinalTotal);
    }

    [Fact]
    public async Task CalculateAsync_ReportsMissingRulesInsteadOfPayingUnpricedRecords()
    {
        await using var database = await TestDatabase.CreateAsync(); var seed = await SeedAsync(database.Db);
        database.Db.ProductionRecords.Add(NewRecord(seed, seed.AttainmentSpecification, new DateTime(2026, 7, 1), 100m)); await database.Db.SaveChangesAsync();
        var report = await new WageCalculationService(database.Db).CalculateAsync(seed.Period.Id);
        Assert.NotNull(report); Assert.Equal("MISSING_RULE", Assert.Single(report.Issues).Code); Assert.Empty(report.Lines); Assert.Equal(0m, report.PieceworkTotal);
    }

    internal static async Task<SeedData> SeedAsync(AppDbContext db)
    {
        var period = new WagePeriod { Year = 2026, Month = 7, Budget = 8m, PlannedWorkdays = 2, PlannedHeadcount = 1 };
        var employee = new Employee { Code = "E001", Name = "员工一" };
        var machine = new Machine { Code = "M0001", Name = "一号机" };
        var material = new Material { Code = "P000001", Name = "测试物料", LegacySpecification = "-", LegacyBuckleCount = 0 };
        db.AddRange(period, employee, machine, material); await db.SaveChangesAsync();
        var attainment = new MaterialSpecification { Code = "P000001-S0001", MaterialId = material.Id, Description = "四扣", BuckleCount = 4m };
        var direct = new MaterialSpecification { Code = "P000001-S0002", MaterialId = material.Id, Description = "两扣", BuckleCount = 2m };
        db.AddRange(attainment, direct); await db.SaveChangesAsync();
        var attainmentLink = new MachineSpecification { MachineId = machine.Id, MaterialSpecificationId = attainment.Id };
        var directLink = new MachineSpecification { MachineId = machine.Id, MaterialSpecificationId = direct.Id };
        db.AddRange(attainmentLink, directLink); db.WagePeriodWorkdays.AddRange(new WagePeriodWorkday { WagePeriodId = period.Id, WorkDate = new DateTime(2026, 7, 1) }, new WagePeriodWorkday { WagePeriodId = period.Id, WorkDate = new DateTime(2026, 7, 2) }); await db.SaveChangesAsync();
        return new SeedData(period, employee, machine, material, attainment, direct, attainmentLink, directLink);
    }
    internal static PricingRule NewRule(SeedData seed, MachineSpecification link, PricingMode mode, decimal? wage, decimal target, decimal? direct) => new() { WagePeriodId = seed.Period.Id, MachineSpecificationId = link.Id, MachineId = seed.Machine.Id, MaterialId = seed.Material.Id, Mode = mode, TargetDailyWage = wage, DefaultTargetBuckleCount = target, DirectPieceRate = direct };
    internal static ProductionRecord NewRecord(SeedData seed, MaterialSpecification specification, DateTime date, decimal quantity) => new() { WagePeriodId = seed.Period.Id, EmployeeId = seed.Employee.Id, MachineId = seed.Machine.Id, MaterialId = seed.Material.Id, MaterialSpecificationId = specification.Id, WorkDate = date, Quantity = quantity, UpdatedBy = "clerk" };
    internal sealed record SeedData(WagePeriod Period, Employee Employee, Machine Machine, Material Material, MaterialSpecification AttainmentSpecification, MaterialSpecification DirectSpecification, MachineSpecification AttainmentLink, MachineSpecification DirectLink);
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _directory;
    private TestDatabase(string directory, string connectionString, AppDbContext db) { _directory = directory; ConnectionString = connectionString; Db = db; }
    public string ConnectionString { get; }
    public AppDbContext Db { get; }
    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "piecework-report-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "test.db"), DefaultTimeout = 30 }.ToString();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options); await db.Database.EnsureCreatedAsync(); return new TestDatabase(directory, connectionString, db);
    }
    public DatabaseBackupService CreateBackupService() => new(ConnectionString, _directory);
    public ExcelService CreateExcelService() => new(Db, new WageCalculationService(Db), CreateBackupService(), new CodeGenerationService(Db), new ExportInvalidationService(Db));
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); SqliteConnection.ClearAllPools(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
