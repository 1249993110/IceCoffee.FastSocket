using IceCoffee.FastSocket;
using IceCoffee.FastSocket.Tcp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace UnitTest
{
    class TestTcpServer : TcpServer
    {
        public TestTcpServer(string address, int port, TcpServerOptions options = null) : base(address, port, options)
        {
        }

        protected override void OnException(SocketException socketException)
        {
            Console.WriteLine(socketException);
        }

        protected override void OnStarted()
        {
            Console.WriteLine("Started");
        }

        protected override void OnStopped()
        {
            Console.WriteLine("Stopped");
        }

        protected override TcpSession CreateSession()
        {
            return new TestTcpSession(this);
        }

        protected override void OnSessionClosed(TcpSession session)
        {
            Console.WriteLine("OnSessionClosed " + session.SessionId);
        }

        protected override void OnSessionStarted(TcpSession session)
        {
            Console.WriteLine("OnSessionStarted " + session.SessionId);
        }
    }
}
