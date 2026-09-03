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

        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeoutCts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-requested cancellation: propagate so a superseded snapshot stops early.
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;   // our own timeout — port is not answering promptly
        }
        catch (SocketException)
        {
            return false;   // connection refused: nothing listening. The common, fast path.
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        // The using disposes the client only after the await has completed, one way or another.
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
    /// Synchronous liveness probe, for the CLI and other genuinely synchronous callers.
    /// Blocks the calling thread by design — never call this from the UI thread or from inside a
    /// loop over many services (use <see cref="AreListeningAsync"/> for that).
    /// </summary>
    public static bool IsListening(int port, int timeoutMs = 400)
    {
        try
        {
            // GetAwaiter().GetResult() rather than .Wait(ms): the timeout is enforced *inside* the
            // async method via CancelAfter, so this returns as soon as the real answer is known and
            // never abandons a pending connect.
            return IsListeningAsync(port, timeoutMs).GetAwaiter().GetResult();
        }
        catch { return false; }
    }
}
