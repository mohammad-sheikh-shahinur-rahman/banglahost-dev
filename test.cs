using System;
using System.Threading.Tasks;
class Test
{
    public static void Run(Action a) { Console.WriteLine("Action"); }
    public static void Run(Func<Task> f) { Console.WriteLine("Func<Task>"); }
    public static async Task Main()
    {
        Run(async () => { await Task.Delay(1); });
    }
}
