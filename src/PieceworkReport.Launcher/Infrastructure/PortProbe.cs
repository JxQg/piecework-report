using System.Net;
using System.Net.Sockets;

namespace PieceworkReport.Launcher.Infrastructure;

public static class PortProbe
{
    public static bool IsAvailable(int port)
    {
        if (port is < 1024 or > 65535) return false;
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Server.ExclusiveAddressUse = true;
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
