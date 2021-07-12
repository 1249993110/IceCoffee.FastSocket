using IceCoffee.FastSocket.Tcp;
using System;
using System.Collections.Generic;
using System.Text;

namespace UnitTest
{
    class TestTcpSession : TcpSession
    {
        public TestTcpSession(TcpServer tcpServer) : base(tcpServer)
        {
        }

        protected override void OnReceived()
        {
            byte[] data = ReadBuffer.ReadAll();
            string message = Encoding.UTF8.GetString(data);
            Console.WriteLine(message);

            SendAsync(data);
            Close();
        }
    }
}
