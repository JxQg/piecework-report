using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PieceworkReport.Launcher.Infrastructure;

public sealed record LanAddress(string Address, string AdapterName, bool HasDefaultGateway)
{
    public override string ToString() => $"{Address}  ({AdapterName})";
}

public static class NetworkAddressService
{
    public static IReadOnlyList<LanAddress> GetLanAddresses()
    {
        var result = new List<LanAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties properties;
            try { properties = adapter.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            var hasGateway = properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any));
            foreach (var address in properties.UnicastAddresses.Select(x => x.Address).Where(IsPrivateIpv4))
                result.Add(new LanAddress(address.ToString(), adapter.Name, hasGateway));
        }
        return result.OrderByDescending(x => x.HasDefaultGateway).ThenBy(x => x.AdapterName, StringComparer.CurrentCulture).ThenBy(x => x.Address, StringComparer.Ordinal).ToList();
    }

    public static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168;
    }
}
