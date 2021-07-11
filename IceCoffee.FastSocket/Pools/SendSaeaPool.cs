using IceCoffee.Common.Pools;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    internal class SendSaeaPool : ConnectionPool<SocketAsyncEventArgs>
    {
        private readonly EventHandler<SocketAsyncEventArgs> _asyncCompletedEventHandler;

        public SendSaeaPool(EventHandler<SocketAsyncEventArgs> asyncCompletedEventHandler)
        {
            this._asyncCompletedEventHandler = asyncCompletedEventHandler;

            base.Min = Environment.ProcessorCount;
            if (base.Min < 2)
            {
                base.Min = 2;
            }

            base.Max = int.MaxValue;

            base.IdleTime = 60;
            base.AllIdleTime = 600;
        }

        protected override SocketAsyncEventArgs Create()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += _asyncCompletedEventHandler;
            return saea;
        }
    }
}
