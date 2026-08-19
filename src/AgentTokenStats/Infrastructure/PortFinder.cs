using System.Net;
using System.Net.Sockets;

namespace AgentTokenStats.Infrastructure;

public static class PortFinder
{
    public const int PreferredPort = 17821;

    public static int Find(int preferred = PreferredPort, int span = 20)
    {
        for (var port = preferred; port < preferred + span; port++)
        {
            if (IsFree(port))
                return port;
        }

        throw new InvalidOperationException($"No free loopback port in {preferred}..{preferred + span - 1}.");
    }

    private static bool IsFree(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
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
