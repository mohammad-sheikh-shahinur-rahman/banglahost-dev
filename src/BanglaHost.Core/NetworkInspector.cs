using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace BanglaHost.Core;

public record PortInfo(int Port, string Protocol, string ProcessName, int ProcessId, string State);
public record NetworkStat(string Interface, long BytesSent, long BytesReceived, long Speed, string Status);

public static class NetworkInspector
{
    public static List<PortInfo> GetListeningPorts()
    {
        var list = new List<PortInfo>();
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            
            // TCP listeners
            foreach (var ep in props.GetActiveTcpListeners())
            {
                list.Add(new PortInfo(ep.Port, "TCP", "", 0, "LISTENING"));
            }
            
            // Active TCP connections
            foreach (var conn in props.GetActiveTcpConnections())
            {
                list.Add(new PortInfo(conn.LocalEndPoint.Port, "TCP", "", 0, conn.State.ToString()));
            }
            
            // UDP listeners
            foreach (var ep in props.GetActiveUdpListeners())
            {
                list.Add(new PortInfo(ep.Port, "UDP", "", 0, "LISTENING"));
            }
        }
        catch { }
        
        return list.DistinctBy(p => (p.Port, p.Protocol)).OrderBy(p => p.Port).ToList();
    }

    public static List<NetworkStat> GetInterfaceStats()
    {
        var list = new List<NetworkStat>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var stats = ni.GetIPv4Statistics();
                list.Add(new NetworkStat(
                    Interface: ni.Name,
                    BytesSent: stats.BytesSent,
                    BytesReceived: stats.BytesReceived,
                    Speed: ni.Speed,
                    Status: ni.OperationalStatus.ToString()
                ));
            }
        }
        catch { }
        return list;
    }

    public static async Task<string> RunNetstatAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "Failed to run netstat.";
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public static PerformanceSnapshot GetPerformanceSnapshot()
    {
        var proc = Process.GetCurrentProcess();
        
        // Use Process CPU time instead of PerformanceCounter
        double cpuPercent = 0;
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = proc.TotalProcessorTime;
            System.Threading.Thread.Sleep(200);
            var endTime = DateTime.UtcNow;
            var endCpuUsage = proc.TotalProcessorTime;
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            cpuPercent = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100.0;
        }
        catch { }

        long totalMemory = 0;
        long availableMemory = 0;
        try
        {
            // Use GC info and process info
            var gcInfo = GC.GetGCMemoryInfo();
            totalMemory = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;
            availableMemory = (gcInfo.TotalAvailableMemoryBytes - proc.WorkingSet64) / 1024 / 1024;
            if (availableMemory < 0) availableMemory = 0;
        }
        catch { }

        return new PerformanceSnapshot(
            CpuPercent: cpuPercent,
            TotalMemoryMB: totalMemory,
            AvailableMemoryMB: availableMemory,
            AppMemoryMB: proc.WorkingSet64 / 1024 / 1024,
            ThreadCount: proc.Threads.Count,
            HandleCount: proc.HandleCount
        );
    }
}

public record PerformanceSnapshot(double CpuPercent, long TotalMemoryMB, long AvailableMemoryMB, long AppMemoryMB, int ThreadCount, int HandleCount);
