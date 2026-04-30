using System.Net;
using System.Net.Sockets;

namespace Shade.Services;

/// Quick check for whether a TCP port is free on a given bind host.
public static class PortAvailability
{
    public static bool IsAvailable(int port, string host)
    {
        if (port <= 0 || port >= 65536) return false;
        IPAddress bind = host switch
        {
            "" or "0.0.0.0" or "*" => IPAddress.Any,
            "localhost" or "127.0.0.1" => IPAddress.Loopback,
            _ => IPAddress.Any,
        };
        try
        {
            var l = new TcpListener(bind, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    public static int FindAvailable(int preferred, string host)
    {
        for (int offset = 0; offset < 100; offset++)
        {
            var c = preferred + offset;
            if (c > 65535) break;
            if (IsAvailable(c, host)) return c;
        }
        return preferred;
    }

    public static (int http, int socks) FindAvailablePair(int httpPort, int socksPort, string host)
    {
        var http = FindAvailable(httpPort, host);
        var socks = FindAvailable(socksPort, host);
        if (socks == http) socks = FindAvailable(socks + 1, host);
        return (http, socks);
    }
}
