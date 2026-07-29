using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace BanglaHost.Core;

public static class NetUtils
{
    /// <summary>
    /// Checks if a TCP port is currently available (not listening or established) on all local interfaces.
    /// </summary>
    public static bool IsPortAvailable(int port)
    {
        bool isAvailable = true;
        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            
            // Check active TCP listeners
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            if (tcpListeners.Any(endpoint => endpoint.Port == port))
                isAvailable = false;
            
            // Check active TCP connections
            if (isAvailable)
            {
                var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
                if (tcpConnections.Any(conn => conn.LocalEndPoint.Port == port))
                    isAvailable = false;
            }
        }
        catch { }
        return isAvailable;
    }
}
