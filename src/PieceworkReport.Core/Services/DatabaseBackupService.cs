using Microsoft.Data.Sqlite;

namespace PieceworkReport.Core.Services;

public sealed class DatabaseBackupService(string connectionString, string dataDirectory)
{
    private readonly string _backupDirectory = Path.Combine(dataDirectory, "backups");

    public async Task<string?> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!File.Exists(builder.DataSource)) return null;
        Directory.CreateDirectory(_backupDirectory);
        var safeReason = string.Concat(reason.Where(character => char.IsLetterOrDigit(character) || character == '-'));
        var backupPath = Path.Combine(_backupDirectory, $"piecework-{DateTime.Now:yyyyMMdd-HHmmss}-{safeReason}.db");
        await using var source = new SqliteConnection(connectionString);
        await using var destination = new SqliteConnection($"Data Source={backupPath}");
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        return backupPath;
    }
}
