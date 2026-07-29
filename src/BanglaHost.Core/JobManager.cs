using System;
using System.Diagnostics;

namespace BanglaHost.Core;

public static class JobManager
{
    private static readonly JobObject _globalJob = new JobObject();

    /// <summary>
    /// Adds a process to the global job object so it gets killed when the app closes or crashes.
    /// </summary>
    public static void Add(Process process)
    {
        try
        {
            _globalJob.AddProcess(process);
        }
        catch
        {
            // Ignore if process already exited or access denied
        }
    }
}
