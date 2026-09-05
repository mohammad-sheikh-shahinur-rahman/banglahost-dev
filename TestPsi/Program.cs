using System;
using System.Diagnostics;

class Program {
    static void Main() {
        try {
            var psi = new ProcessStartInfo { FileName = @"i:\BanglaHost\src\BanglaHost.Elevate\bin\Release\net8.0\banglahost-elevate.exe", UseShellExecute = false };
            psi.ArgumentList.Add("hosts-add");
            psi.ArgumentList.Add("test-no-shell.test");
            var p = Process.Start(psi);
            p.WaitForExit();
            Console.WriteLine("OK: Exit code " + p.ExitCode);
        } catch (Exception ex) {
            Console.WriteLine("FAIL: " + ex.GetType().Name + " " + ex.Message);
        }
    }
}
