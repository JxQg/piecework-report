using System.Text.Json;

namespace PieceworkReport.Launcher.Infrastructure;

public sealed class LauncherSettings
{
    public int Port { get; set; } = 5188;
    public string? SelectedLanAddress { get; set; }
}

public sealed class LauncherSettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LauncherSettings Load()
    {
        if (!File.Exists(path)) return new LauncherSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions) ?? new LauncherSettings();
            if (settings.Port is < 1024 or > 65535) settings.Port = 5188;
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("启动器配置文件无效，请检查 launcher.json。", exception);
        }
    }

    public void Save(LauncherSettings settings)
    {
        if (settings.Port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(settings.Port));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, true);
    }
}
