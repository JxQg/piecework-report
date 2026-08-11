using System.Reflection;

namespace PieceworkReport.Core.Services;

public static class ProductInformation
{
    public const string ProductName = "PieceworkReport";
    public const string DisplayName = "计件工资管理";
    public const int CurrentSchemaVersion = 3;

    public static string Version
    {
        get
        {
            var version = typeof(ProductInformation).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(ProductInformation).Assembly.GetName().Version?.ToString()
                ?? "0.0.0";
            return version.Split('+')[0];
        }
    }

    public static string BuildDateUtc => typeof(ProductInformation).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "BuildDateUtc")?.Value ?? "unknown";
}
