using System;
using System.Diagnostics;
class P { static void Main() {
var psi = new ProcessStartInfo { FileName = "cmd.exe", UseShellExecute = true };
psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("echo HELLO > test.txt");
Process.Start(psi).WaitForExit();
}}
