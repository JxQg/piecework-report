using Microsoft.EntityFrameworkCore;
using PieceworkReport.Core.Data;

namespace PieceworkReport.Core.Services;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "piecework-report.db");
        BackupDirectory = Path.Combine(DataDirectory, "backups");
        ImportDirectory = Path.Combine(DataDirectory, "imports");
        ExportDirectory = Path.Combine(DataDirectory, "exports");
        KeyDirectory = Path.Combine(DataDirectory, "keys");
        ConnectionString = $"Data Source={DatabasePath}";
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string BackupDirectory { get; }
    public string ImportDirectory { get; }
    public string ExportDirectory { get; }
    public string KeyDirectory { get; }
    public string ConnectionString { get; }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(ImportDirectory);
        Directory.CreateDirectory(ExportDirectory);
        Directory.CreateDirectory(KeyDirectory);
    }

    public AppDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
}
