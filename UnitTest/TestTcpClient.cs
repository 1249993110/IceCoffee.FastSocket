using IceCoffee.FastSocket;
using IceCoffee.FastSocket.Tcp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UnitTest
{
    class TestTcpClient : TcpClient
    {
        public TestTcpClient(string address, int port, TcpClientOptions options = null) : base(address, port, options)
        {
        }

        protected override void OnConnectionStateChanged(ConnectionState connectionState)
        {
            Console.WriteLine("ConnectionStateChanged: " + connectionState);
        }

        protected override void OnException(SocketException socketException)
        {
            Console.WriteLine(socketException);
        }

        protected override void OnReceived()
        {
            byte[] data = ReadBuffer.ReadAll();
            string message = Encoding.UTF8.GetString(data);
            Console.WriteLine(message);

            SendAsync(data);
            DisconnectAsync();
        }
    }
}
