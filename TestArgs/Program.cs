using System;
using System.IO;

class Program {
    static void Main(string[] args) {
        File.WriteAllLines("args_out.txt", args);
    }
}
