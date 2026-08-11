using PieceworkReport.Core.Services;

namespace PieceworkReport.Launcher.Infrastructure;

public sealed class LauncherPaths
{
    private LauncherPaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        DataDirectory = Path.Combine(RootDirectory, "data");
        ConfigurationDirectory = Path.Combine(RootDirectory, "config");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        SettingsPath = Path.Combine(ConfigurationDirectory, "launcher.json");
        CorePaths = new ApplicationPaths(DataDirectory);
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string ConfigurationDirectory { get; }
    public string LogDirectory { get; }
    public string SettingsPath { get; }
    public ApplicationPaths CorePaths { get; }

    public static LauncherPaths Create(IReadOnlyList<string> args)
    {
        var customIndex = args.ToList().FindIndex(x => string.Equals(x, "--data-root", StringComparison.OrdinalIgnoreCase));
        if (customIndex >= 0 && customIndex + 1 < args.Count) return new LauncherPaths(args[customIndex + 1]);
        return new LauncherPaths(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PieceworkReport"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(ConfigurationDirectory);
        Directory.CreateDirectory(LogDirectory);
        CorePaths.EnsureDirectories();
    }

    public string FindWebExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web", "PieceworkReport.Web.exe"),
            Path.Combine(AppContext.BaseDirectory, "PieceworkReport.Web.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PieceworkReport.Web", "bin", "Debug", "net8.0", "PieceworkReport.Web.exe"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
