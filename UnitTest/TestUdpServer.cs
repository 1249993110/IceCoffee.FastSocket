using IceCoffee.FastSocket;
using IceCoffee.FastSocket.Udp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UnitTest
{
    class TestUdpServer : UdpServer
    {
        public TestUdpServer(string address, int port, UdpServerOptions options = null) : base(address, port, options)
        {
        }

        protected override void OnException(Exception exception)
        {
            Console.WriteLine(exception);
        }

        protected override void OnStarted()
        {
            Console.WriteLine("Started");
        }

        protected override void OnStopped()
        {
            Console.WriteLine("Stopped");
        }

        protected override void OnReceived(EndPoint endpoint, byte[] buffer, int size)
        {
            Console.WriteLine("OnReceived " + Encoding.UTF8.GetString(buffer, 0, size));
            SendAsync(endpoint, buffer, 0 , size);
        }
    }
}
