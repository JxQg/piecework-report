using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Tests;

public sealed class SchemaMigratorTests
{
    [Fact]
    public async Task UpgradeAsync_BacksUpAndMigratesLegacyMaterialRulesAndRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "piecework-v1-migration", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "piecework-report.db"); var backups = Path.Combine(directory, "backups"); var connectionString = $"Data Source={databasePath}";
        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                var sql = """
                    CREATE TABLE Materials (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL, Specification TEXT NOT NULL, BuckleCount TEXT NOT NULL, IsActive INTEGER NOT NULL);
                    CREATE TABLE Machines (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL, IsActive INTEGER NOT NULL);
                    CREATE TABLE WagePeriods (Id INTEGER PRIMARY KEY AUTOINCREMENT, Year INTEGER NOT NULL, Month INTEGER NOT NULL, Budget TEXT NOT NULL, PlannedWorkdays INTEGER NOT NULL, PlannedHeadcount INTEGER NOT NULL, ExportOutdated INTEGER NOT NULL, UpdatedAt TEXT NOT NULL);
                    CREATE TABLE PricingRules (Id INTEGER PRIMARY KEY AUTOINCREMENT, WagePeriodId INTEGER NOT NULL, MachineId INTEGER NOT NULL, MaterialId INTEGER NOT NULL, Mode INTEGER NOT NULL, DirectPieceRate TEXT NULL, StandardDailyPieces TEXT NULL, Note TEXT NULL);
                    CREATE TABLE ProductionRecords (Id INTEGER PRIMARY KEY AUTOINCREMENT, WagePeriodId INTEGER NOT NULL, WorkDate TEXT NOT NULL, EmployeeId INTEGER NOT NULL, MachineId INTEGER NOT NULL, MaterialId INTEGER NOT NULL, Quantity TEXT NOT NULL, Note TEXT NULL, Source TEXT NOT NULL, UpdatedBy TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                    INSERT INTO Materials VALUES (1, 'P001', '底板', '100*200', '4', 1);
                    INSERT INTO Machines VALUES (1, 'M001', '旧机器', 1);
                    INSERT INTO WagePeriods VALUES (1, 2026, 8, '88000', 23, 16, 1, '2026-08-01');
                    INSERT INTO PricingRules VALUES (1, 1, 1, 1, 0, NULL, '1000', NULL);
                    INSERT INTO ProductionRecords VALUES (1, 1, '2026-08-03', 1, 1, 1, '10', NULL, 'Manual', 'clerk', '2026-08-03');
                    """;
                await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
            }
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options);
            await SchemaMigrator.UpgradeAsync(db, databasePath, backups);
            Assert.Single(Directory.GetFiles(backups, "*-before-v2.db"));
            Assert.Equal("P001-S0001", await db.MaterialSpecifications.Select(x => x.Code).SingleAsync());
            Assert.Equal(4_000m, await db.PricingRules.Select(x => x.DefaultTargetBuckleCount).SingleAsync());
            Assert.Equal(new DateTime(2026, 8, 3), await db.WagePeriodWorkdays.Select(x => x.WorkDate).SingleAsync());
            Assert.NotNull(await db.ProductionRecords.Select(x => x.MaterialSpecificationId).SingleAsync());
            Assert.Equal(ProductInformation.CurrentSchemaVersion, await SchemaMigrator.GetCurrentVersionAsync(db));
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
