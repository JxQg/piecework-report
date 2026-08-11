using System.Net;
using System.Net.Sockets;
using PieceworkReport.Launcher.Infrastructure;

namespace PieceworkReport.Launcher.Tests;

public sealed class LauncherInfrastructureTests
{
    [Theory]
    [InlineData("10.2.3.4", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.10.8", true)]
    [InlineData("169.254.10.8", false)]
    [InlineData("127.0.0.1", false)]
    public void PrivateIpv4Filtering_IsDeterministic(string value, bool expected) =>
        Assert.Equal(expected, NetworkAddressService.IsPrivateIpv4(IPAddress.Parse(value)));

    [Fact]
    public void SettingsStore_WritesAtomicallyAndRejectsInvalidPorts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "piecework-launcher-settings", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "launcher.json"); var store = new LauncherSettingsStore(path);
            store.Save(new LauncherSettings { Port = 6200, SelectedLanAddress = "192.168.1.2" });
            var loaded = store.Load(); Assert.Equal(6200, loaded.Port); Assert.Equal("192.168.1.2", loaded.SelectedLanAddress); Assert.False(File.Exists(path + ".tmp"));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new LauncherSettings { Port = 80 }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void PortProbe_DetectsAnOccupiedPort()
    {
        using var listener = new TcpListener(IPAddress.Any, 0); listener.Server.ExclusiveAddressUse = true; listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.False(PortProbe.IsAvailable(port));
        listener.Stop();
        Assert.True(PortProbe.IsAvailable(port));
    }

    [Fact]
    public void UpdateVersionComparison_IgnoresFileVersionRevision()
    {
        Assert.Equal(
            UpdatePackageInspector.NormalizeVersion(new Version(2, 1, 0)),
            UpdatePackageInspector.NormalizeVersion(new Version(2, 1, 0, 0)));
        Assert.True(UpdatePackageInspector.IsExpectedProduct(
            "PieceworkReport Setup                                       ",
            "PieceworkReport                                             "));
    }
}
