using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Core.Data;

public static class SchemaMigrator
{
    public static async Task UpgradeAsync(AppDbContext db, string databasePath, string backupDirectory)
    {
        await db.Database.EnsureCreatedAsync();
        var connectionString = db.Database.GetConnectionString() ?? $"Data Source={databasePath}";

        bool needsV2;
        bool needsV3;
        await using (var inspection = new SqliteConnection(connectionString))
        {
            await inspection.OpenAsync();
            needsV2 = !await TableExistsAsync(inspection, "MaterialSpecifications");
            needsV3 = !await TableExistsAsync(inspection, "SchemaVersions")
                || !await TableExistsAsync(inspection, "SecurityAuditEntries")
                || await TableExistsAsync(inspection, "Users") && !await ColumnExistsAsync(inspection, "Users", "SecurityStamp");
        }

        if (needsV2 || needsV3)
        {
            Directory.CreateDirectory(backupDirectory);
            await new DatabaseBackupService(connectionString, Path.GetDirectoryName(databasePath)!)
                .CreateBackupAsync(needsV2 ? "before-v2" : "before-v3");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        if (needsV2)
        {
            foreach (var sql in V2Commands) await ExecuteAsync(connection, transaction, sql);
        }

        await ExecuteAsync(connection, transaction,
            "CREATE TABLE IF NOT EXISTS SchemaVersions (Id INTEGER NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY, Version INTEGER NOT NULL, UpdatedAt TEXT NOT NULL)");
        await ExecuteAsync(connection, transaction,
            "CREATE TABLE IF NOT EXISTS SecurityAuditEntries (Id INTEGER NOT NULL CONSTRAINT PK_SecurityAuditEntries PRIMARY KEY AUTOINCREMENT, EventType TEXT NOT NULL, Username TEXT NOT NULL, Detail TEXT NULL, CreatedAt TEXT NOT NULL)");

        if (await TableExistsAsync(connection, "Users", transaction) && !await ColumnExistsAsync(connection, "Users", "SecurityStamp", transaction))
        {
            await ExecuteAsync(connection, transaction, "ALTER TABLE Users ADD COLUMN SecurityStamp TEXT NOT NULL DEFAULT ''");
            await ExecuteAsync(connection, transaction, "UPDATE Users SET SecurityStamp = lower(hex(randomblob(16))) WHERE SecurityStamp = ''");
        }

        await ExecuteAsync(connection, transaction,
            $"INSERT INTO SchemaVersions (Id, Version, UpdatedAt) VALUES (1, {ProductInformation.CurrentSchemaVersion}, datetime('now')) " +
            $"ON CONFLICT(Id) DO UPDATE SET Version = {ProductInformation.CurrentSchemaVersion}, UpdatedAt = datetime('now')");
        await transaction.CommitAsync();
    }

    public static async Task<int> GetCurrentVersionAsync(AppDbContext db)
    {
        if (!await db.Database.CanConnectAsync()) return 0;
        return await db.SchemaVersions.AsNoTracking().Where(x => x.Id == 1).Select(x => x.Version).SingleOrDefaultAsync();
    }

    private static readonly string[] V2Commands =
    [
        "CREATE TABLE MaterialSpecifications (Id INTEGER NOT NULL CONSTRAINT PK_MaterialSpecifications PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, MaterialId INTEGER NOT NULL, Description TEXT NOT NULL, BuckleCount TEXT NOT NULL, Note TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 1, CONSTRAINT FK_MaterialSpecifications_Materials_MaterialId FOREIGN KEY (MaterialId) REFERENCES Materials (Id) ON DELETE RESTRICT)",
        "CREATE UNIQUE INDEX IX_MaterialSpecifications_Code ON MaterialSpecifications (Code)",
        "CREATE UNIQUE INDEX IX_MaterialSpecifications_MaterialId_Description ON MaterialSpecifications (MaterialId, Description)",
        "CREATE TABLE MachineSpecifications (Id INTEGER NOT NULL CONSTRAINT PK_MachineSpecifications PRIMARY KEY AUTOINCREMENT, MachineId INTEGER NOT NULL, MaterialSpecificationId INTEGER NOT NULL, IsActive INTEGER NOT NULL DEFAULT 1, Note TEXT NULL, CONSTRAINT FK_MachineSpecifications_Machines_MachineId FOREIGN KEY (MachineId) REFERENCES Machines (Id) ON DELETE RESTRICT, CONSTRAINT FK_MachineSpecifications_MaterialSpecifications_MaterialSpecificationId FOREIGN KEY (MaterialSpecificationId) REFERENCES MaterialSpecifications (Id) ON DELETE RESTRICT)",
        "CREATE UNIQUE INDEX IX_MachineSpecifications_MachineId_MaterialSpecificationId ON MachineSpecifications (MachineId, MaterialSpecificationId)",
        "CREATE TABLE WagePeriodWorkdays (Id INTEGER NOT NULL CONSTRAINT PK_WagePeriodWorkdays PRIMARY KEY AUTOINCREMENT, WagePeriodId INTEGER NOT NULL, WorkDate TEXT NOT NULL, CONSTRAINT FK_WagePeriodWorkdays_WagePeriods_WagePeriodId FOREIGN KEY (WagePeriodId) REFERENCES WagePeriods (Id) ON DELETE CASCADE)",
        "CREATE UNIQUE INDEX IX_WagePeriodWorkdays_WagePeriodId_WorkDate ON WagePeriodWorkdays (WagePeriodId, WorkDate)",
        "CREATE TABLE EmployeePricingOverrides (Id INTEGER NOT NULL CONSTRAINT PK_EmployeePricingOverrides PRIMARY KEY AUTOINCREMENT, PricingRuleId INTEGER NOT NULL, EmployeeId INTEGER NOT NULL, TargetBuckleCount TEXT NOT NULL, CONSTRAINT FK_EmployeePricingOverrides_PricingRules_PricingRuleId FOREIGN KEY (PricingRuleId) REFERENCES PricingRules (Id) ON DELETE CASCADE, CONSTRAINT FK_EmployeePricingOverrides_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES Employees (Id) ON DELETE RESTRICT)",
        "CREATE UNIQUE INDEX IX_EmployeePricingOverrides_PricingRuleId_EmployeeId ON EmployeePricingOverrides (PricingRuleId, EmployeeId)",
        "CREATE TABLE CodeSequences (Name TEXT NOT NULL CONSTRAINT PK_CodeSequences PRIMARY KEY, NextValue INTEGER NOT NULL)",
        "ALTER TABLE PricingRules ADD COLUMN MachineSpecificationId INTEGER NULL",
        "ALTER TABLE PricingRules ADD COLUMN TargetDailyWage TEXT NULL",
        "ALTER TABLE PricingRules ADD COLUMN DefaultTargetBuckleCount TEXT NULL",
        "ALTER TABLE ProductionRecords ADD COLUMN MaterialSpecificationId INTEGER NULL",
        "INSERT INTO MaterialSpecifications (Code, MaterialId, Description, BuckleCount, IsActive) SELECT Code || '-S0001', Id, Specification, BuckleCount, IsActive FROM Materials",
        "INSERT OR IGNORE INTO MachineSpecifications (MachineId, MaterialSpecificationId, IsActive) SELECT DISTINCT p.MachineId, s.Id, 1 FROM PricingRules p JOIN MaterialSpecifications s ON s.MaterialId = p.MaterialId",
        "INSERT OR IGNORE INTO MachineSpecifications (MachineId, MaterialSpecificationId, IsActive) SELECT DISTINCT p.MachineId, s.Id, 1 FROM ProductionRecords p JOIN MaterialSpecifications s ON s.MaterialId = p.MaterialId",
        "UPDATE PricingRules SET MachineSpecificationId = (SELECT ms.Id FROM MachineSpecifications ms JOIN MaterialSpecifications s ON s.Id = ms.MaterialSpecificationId WHERE ms.MachineId = PricingRules.MachineId AND s.MaterialId = PricingRules.MaterialId LIMIT 1)",
        "UPDATE PricingRules SET TargetDailyWage = (SELECT CASE WHEN PlannedWorkdays > 0 AND PlannedHeadcount > 0 THEN Budget / PlannedWorkdays / PlannedHeadcount ELSE 0 END FROM WagePeriods w WHERE w.Id = PricingRules.WagePeriodId)",
        "UPDATE PricingRules SET DefaultTargetBuckleCount = StandardDailyPieces * (SELECT BuckleCount FROM Materials m WHERE m.Id = PricingRules.MaterialId) WHERE StandardDailyPieces IS NOT NULL",
        "UPDATE ProductionRecords SET MaterialSpecificationId = (SELECT Id FROM MaterialSpecifications s WHERE s.MaterialId = ProductionRecords.MaterialId LIMIT 1)",
        "INSERT OR IGNORE INTO WagePeriodWorkdays (WagePeriodId, WorkDate) SELECT DISTINCT WagePeriodId, date(WorkDate) FROM ProductionRecords",
        "DROP INDEX IF EXISTS IX_PricingRules_WagePeriodId_MachineId_MaterialId",
        "DROP INDEX IF EXISTS IX_ProductionRecords_WagePeriodId_WorkDate_EmployeeId_MachineId_MaterialId",
        "CREATE UNIQUE INDEX IX_PricingRules_WagePeriodId_MachineSpecificationId ON PricingRules (WagePeriodId, MachineSpecificationId)",
        "CREATE UNIQUE INDEX IX_ProductionRecords_WagePeriodId_WorkDate_EmployeeId_MachineId_MaterialSpecificationId ON ProductionRecords (WagePeriodId, WorkDate, EmployeeId, MachineId, MaterialSpecificationId)",
        "INSERT INTO CodeSequences (Name, NextValue) VALUES ('Machine', (SELECT COUNT(*) + 1 FROM Machines))",
        "INSERT INTO CodeSequences (Name, NextValue) VALUES ('Material', (SELECT COUNT(*) + 1 FROM Materials))",
        "INSERT INTO CodeSequences (Name, NextValue) SELECT 'Specification:' || MaterialId, COUNT(*) + 1 FROM MaterialSpecifications GROUP BY MaterialId"
    ];

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info([{tableName.Replace("]", "]]", StringComparison.Ordinal)}])";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
