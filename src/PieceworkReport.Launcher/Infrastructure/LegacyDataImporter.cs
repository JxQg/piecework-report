using Microsoft.Data.Sqlite;

namespace PieceworkReport.Launcher.Infrastructure;

public static class LegacyDataImporter
{
    public static async Task ImportAsync(string sourceDirectory, LauncherPaths targetPaths)
    {
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        var sourceDatabase = Path.Combine(sourceDirectory, "piecework-report.db");
        if (!File.Exists(sourceDatabase)) throw new InvalidOperationException("所选目录不包含 piecework-report.db。");
        if (Path.GetFullPath(targetPaths.DataDirectory).StartsWith(sourceDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("旧数据目录不能包含新的正式数据目录。");
        if (Directory.Exists(targetPaths.DataDirectory) && Directory.EnumerateFileSystemEntries(targetPaths.DataDirectory).Any())
            throw new InvalidOperationException("新的正式数据目录已经包含文件，不能再导入旧数据。");

        await using (var connection = new SqliteConnection($"Data Source={sourceDatabase};Mode=ReadOnly"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0) throw new InvalidOperationException("所选数据库不包含可识别的数据表。");
        }

        var temporaryDirectory = Path.Combine(targetPaths.RootDirectory, $"data-import-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(sourceDirectory, temporaryDirectory);
            if (Directory.Exists(targetPaths.DataDirectory)) Directory.Delete(targetPaths.DataDirectory, false);
            Directory.Move(temporaryDirectory, targetPaths.DataDirectory);
        }
        catch
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            throw;
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
