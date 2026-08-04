using System;
class Program {
    static void Main() {
        try { string s = "a"; var x = s[..^5]; } catch (Exception e) { Console.WriteLine("[..^5]: " + e.Message); }
    }
}
