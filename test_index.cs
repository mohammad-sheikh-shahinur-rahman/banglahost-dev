using System;
class Program
{
    static void Main()
    {
        try { string s = "a"; var x = s[2..]; } catch(Exception e) { Console.WriteLine("s[2..]: " + e.Message); }
        try { string s = "a"; var x = s.Substring(2); } catch(Exception e) { Console.WriteLine("Substring(2): " + e.Message); }
        try { string s = "a"; var x = s[1..]; Console.WriteLine("s[1..]: OK"); } catch(Exception e) { Console.WriteLine("s[1..]: " + e.Message); }
    }
}
