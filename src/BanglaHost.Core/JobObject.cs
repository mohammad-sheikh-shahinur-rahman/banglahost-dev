using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BanglaHost.Core;

/// <summary>
/// Manages a Windows Job Object.
/// A Job Object allows grouping processes together. When the job is closed
/// (or the parent process dies), all child processes in the job are killed.
///
/// Construction never throws: use <see cref="TryCreate"/>. A job object can legitimately be
/// unavailable (nested-job restrictions, an app container, a policy-restricted environment), and
/// when it is, the caller must degrade to pid tracking rather than lose the ability to spawn
/// processes at all.
/// </summary>
public sealed class JobObject : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    private JobObject(IntPtr handle) => _handle = handle;

    /// <summary>Create a job object that kills its members when the handle closes, or return null
    /// with the reason if the OS won't give us one. Never throws.</summary>
    /// <param name="killOnClose">When false the job groups processes for bookkeeping but does NOT
    /// kill them when it closes — used by the CLI, which must leave started services running after
    /// it exits.</param>
    public static JobObject? TryCreate(bool killOnClose, out string error)
    {
        error = "";
        var handle = IntPtr.Zero;
        var extendedInfoPtr = IntPtr.Zero;
        try
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                error = $"CreateJobObject failed (Win32 {Marshal.GetLastWin32Error()})";
                return null;
            }

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = killOnClose ? JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE : 0u,
                },
            };

            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            extendedInfoPtr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            if (!SetInformationJobObject(handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                error = $"SetInformationJobObject failed (Win32 {Marshal.GetLastWin32Error()})";
                CloseHandle(handle);
                return null;
            }

            var job = new JobObject(handle);
            handle = IntPtr.Zero;   // ownership transferred
            return job;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (handle != IntPtr.Zero) { try { CloseHandle(handle); } catch { } }
            return null;
        }
        finally
        {
            // Freed on every path — the original code leaked this whenever the Set call failed.
            if (extendedInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    /// <summary>Assign a process to this job. Returns false (rather than throwing) if the process
    /// has already exited or the OS refuses — both are normal and neither should break the caller.</summary>
    public bool TryAddProcess(Process process)
    {
        if (process is null || _disposed || _handle == IntPtr.Zero) return false;
        try
        {
            // process.Handle throws InvalidOperationException once the process has exited.
            if (process.HasExited) return false;
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            try { CloseHandle(_handle); } catch { }
            _handle = IntPtr.Zero;
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr a, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public Int64 PerProcessUserTimeLimit;
        public Int64 PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public Int64 Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public UInt64 ReadOperationCount;
        public UInt64 WriteOperationCount;
        public UInt64 OtherOperationCount;
        public UInt64 ReadTransferCount;
        public UInt64 WriteTransferCount;
        public UInt64 OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
