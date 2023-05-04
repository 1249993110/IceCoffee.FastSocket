using System;
using System.Data.Common;
using System.Net;
using System.Text;
using System.Threading;

namespace UnitTest
{
    class Program
    {
        static void Main(string[] args)
        {
            TestUdpServer();
        }

        static void TestTcpServer()
        {
            Console.WriteLine("Hello World!");

            TestTcpServer testTcpServer = new TestTcpServer("127.0.0.1", 12345);
            testTcpServer.Start();

            string line;
            do
            {
                line = Console.ReadLine();


            } while (line != "exit");

            testTcpServer.Dispose();

            Console.ReadKey();
        }

        static void TestTcpClient()
        {
            Console.WriteLine("Hello World!");

            TestTcpClient testTcpClient = new TestTcpClient("localhost", 6000);
            testTcpClient.ConnectAsync();
            testTcpClient.ReconnectAsync();
            testTcpClient.ReconnectAsync();
            testTcpClient.ReconnectAsync();
            testTcpClient.ReconnectAsync();

            string line;
            do
            {
                line = Console.ReadLine();

                testTcpClient.SendAsync(Encoding.UTF8.GetBytes(line));


            } while (line != "exit");

            testTcpClient.Dispose();

            Console.ReadKey();
        }

        static void TestUdpServer()
        {
            Console.WriteLine("Hello World!");
            
            TestUdpServer testUdpServer = new TestUdpServer(IPAddress.Any, 10240);
            testUdpServer.Start();
            //TestUdpServer testUdpServer = new TestUdpServer(IPAddress.Any, 0);
            //testUdpServer.Start("239.255.0.1", 10114);

            string line;
            do
            {
                line = Console.ReadLine();
                //testUdpServer.MulticastAsync(Encoding.UTF8.GetBytes(line));

            } while (line != "exit");

            testUdpServer.Dispose();

            Console.ReadKey();
        }

        static void TestUdpClient()
        {
            Console.WriteLine("Hello World!");

            TestUdpClient testUdpClient = new TestUdpClient("10.166.168.201", 10114);
            testUdpClient.Open();

            string line;
            do
            {
                line = Console.ReadLine();
                testUdpClient.SendAsync(Encoding.UTF8.GetBytes(line));

            } while (line != "exit");

            testUdpClient.Dispose();

            Console.ReadKey();
        }
    }
}
