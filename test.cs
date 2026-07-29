using System;
using System.Diagnostics;
using System.Threading;
using BanglaHost.Core;

class Program {
    static void Main() {
        var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 10") { CreateNoWindow = true, UseShellExecute = false });
        JobManager.Add(p);
        Console.WriteLine("Added to job. Waiting 3 sec...");
        Thread.Sleep(3000);
        Console.WriteLine("Exiting. Should kill ping.");
    }
}
