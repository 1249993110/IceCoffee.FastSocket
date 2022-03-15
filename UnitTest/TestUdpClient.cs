using IceCoffee.FastSocket;
using IceCoffee.FastSocket.Udp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UnitTest
{
    class TestUdpClient : UdpClient
    {
        public TestUdpClient(string address, int port, UdpClientOptions options = null) : base(address, port, options)
        {
        }

        protected override void OnExceptionCaught(Exception exception)
        {
            Console.WriteLine(exception);
        }

        protected override void OnOpened()
        {
            Console.WriteLine("OnOpened");
        }

        protected override void OnClosed()
        {
            Console.WriteLine("OnClosed");
        }

        protected override void OnReceived(EndPoint endpoint, byte[] buffer, int size)
        {
            Console.WriteLine("OnReceived " + Encoding.UTF8.GetString(buffer, 0, size));
            SendAsync(endpoint, buffer, 0 , size);
        }
    }
}
