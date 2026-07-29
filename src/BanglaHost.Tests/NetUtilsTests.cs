using System.Net;
using System.Net.Sockets;
using Xunit;
using BanglaHost.Core;

namespace BanglaHost.Tests;

public class NetUtilsTests
{
    [Fact]
    public void IsPortAvailable_WhenPortIsFree_ReturnsTrue()
    {
        // 0 gets an available ephemeral port. We just need a random free port for this test.
        int port = GetFreePort();
        Assert.True(NetUtils.IsPortAvailable(port));
    }

    [Fact]
    public void IsPortAvailable_WhenPortIsInUse_ReturnsFalse()
    {
        int port = GetFreePort();

        // Bind the port explicitly so it is IN USE
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();

        // Now NetUtils should report that the port is NOT available
        Assert.False(NetUtils.IsPortAvailable(port));

        listener.Stop();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
