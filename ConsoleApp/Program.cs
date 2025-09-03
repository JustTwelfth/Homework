using System;
using ClassLibrary;

namespace ConsoleApp
{
    internal class Program
    {
        static void Main()
        {
            var server = new TcpEchoServer();
            server.LogEvent += msg => Console.WriteLine(msg);
            server.Start();
            Console.ReadLine();
            server.Stop();
        }
    }
}