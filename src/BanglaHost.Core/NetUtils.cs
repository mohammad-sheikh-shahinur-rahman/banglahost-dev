using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public static class NetUtils
{
    /// <summary>
    /// Checks if a TCP port is currently available (not listening or established) on all local interfaces.
    ///
    /// Fails CLOSED: if the enumeration throws we report "not available". The previous version
    /// returned true on failure, so a transient WMI/IpHlpApi hiccup told the caller a port in use
    /// by another program was free — and BanglaHost would then bind-fail, or worse, hand a user's
    /// site to whatever was already on port 80. Reporting a false conflict is a visible, recoverable
    /// annoyance; reporting a false vacancy is not.
    /// </summary>
    public static bool IsPortAvailable(int port)
    {
        try
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

            // Check active TCP listeners
            var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();
            if (tcpListeners.Any(endpoint => endpoint.Port == port))
                return false;

            // Check active TCP connections (ignore non-blocking states like TIME_WAIT)
            var tcpConnections = ipGlobalProperties.GetActiveTcpConnections();
            if (tcpConnections.Any(conn => conn.LocalEndPoint.Port == port &&
                conn.State != TcpState.TimeWait &&
                conn.State != TcpState.CloseWait &&
                conn.State != TcpState.Closed &&
                conn.State != TcpState.FinWait1 &&
                conn.State != TcpState.FinWait2 &&
                conn.State != TcpState.Closing))
            {
                return false;
            }

            return true;
        }
        catch
        {
            // Fail closed — see the remarks above.
            return false;
        }
    }

    /// <summary>
    /// Is something accepting connections on this loopback port?
    ///
    /// This replaces the <c>TcpClient.ConnectAsync(...).Wait(ms)</c> idiom that was used for every
    /// service liveness probe. That pattern was the single biggest contributor to the app hanging:
    /// <list type="bullet">
    /// <item>the blocking <c>.Wait</c> pins a thread-pool thread for the whole timeout, while the
    /// socket completion needs a *second* pool thread — so each probe costs two threads,</item>
    /// <item>the <c>TcpClient</c> was disposed while the connect could still be pending, which both
    /// races and can surface later as an unobserved <c>SocketException</c>,</item>
    /// <item>probes ran sequentially, so ~37 services × up to 600 ms is a multi-second stall.</item>
    /// </list>
    /// On a healthy machine loopback refuses instantly and this returns in microseconds. The
    /// timeout only elapses in full when a firewall or AV product filters loopback — which is
    /// exactly the configuration where the old code hung.
    /// </summary>
    public static async Task<bool> IsListeningAsync(int port, int timeoutMs = 400, CancellationToken ct = default)
    {
        if (port <= 0 || port > 65535) return false;
        try
        {
            return await Task.Run(() => IsListening(port, timeoutMs), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Probe many ports at once under a single shared deadline. Used by the dashboard
    /// snapshot, where the sequential version cost seconds.</summary>
    public static async Task<bool[]> AreListeningAsync(int[] ports, int timeoutMs = 400, CancellationToken ct = default)
    {
        if (ports is null || ports.Length == 0) return Array.Empty<bool>();
        var tasks = new Task<bool>[ports.Length];
        for (var i = 0; i < ports.Length; i++) tasks[i] = IsListeningAsync(ports[i], timeoutMs, ct);
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous liveness probe using non-blocking Socket.Poll.
    /// Does not use IOCP or BeginConnect, eliminating thread-pool crash on timeout when disposed.
    /// </summary>
    public static bool IsListening(int port, int timeoutMs = 400)
    {
        if (port <= 0 || port > 65535) return false;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Blocking = false;
            try
            {
                socket.Connect(new IPEndPoint(IPAddress.Loopback, port));
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.WouldBlock or SocketError.InProgress)
            {
                // Socket.Poll takes MICROSECONDS as an int. timeoutMs * 1000 overflows int above
                // ~2147 ms, and a negative microsecond value means "block forever" — so a caller
                // passing a 3 s timeout turned a bounded probe into an unbounded one on a filtered
                // loopback, hanging whatever thread it ran on. Clamped to a safe range.
                const int MaxPollMs = int.MaxValue / 1000;   // ~2147 ms
                var pollMicros = Math.Clamp(timeoutMs, 0, MaxPollMs) * 1000;
                if (socket.Poll(pollMicros, SelectMode.SelectWrite))
                {
                    var err = (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)!;
                    return err == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        catch { return false; }
    }
}
