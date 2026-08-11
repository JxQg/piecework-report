using System.Diagnostics;
using System.Security.Cryptography;
using PieceworkReport.Core.Services;

namespace PieceworkReport.Launcher.Infrastructure;

public sealed record UpdatePackage(string Path, Version Version, string Sha256, bool IsSameVersion);

public static class UpdatePackageInspector
{
    public static async Task<UpdatePackage> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效的安装程序 .exe 文件。");
        var information = FileVersionInfo.GetVersionInfo(path);
        if (!IsExpectedProduct(information.ProductName, information.CompanyName))
            throw new InvalidOperationException("所选文件不是计件工资管理安装包。");
        if (!Version.TryParse(information.FileVersion?.Trim(), out var candidate)) throw new InvalidOperationException("安装包没有有效的文件版本。");
        candidate = NormalizeVersion(candidate);
        if (!Version.TryParse(ProductInformation.Version, out var current)) current = new Version(0, 0, 0);
        current = NormalizeVersion(current);
        if (candidate < current) throw new InvalidOperationException($"不能从 {current} 降级到 {candidate}。");
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        return new UpdatePackage(Path.GetFullPath(path), candidate, hash, candidate == current);
    }

    public static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    public static bool IsExpectedProduct(string? productName, string? companyName) =>
        string.Equals(productName?.Trim(), "PieceworkReport Setup", StringComparison.Ordinal)
        && string.Equals(companyName?.Trim(), "PieceworkReport", StringComparison.Ordinal);
}
