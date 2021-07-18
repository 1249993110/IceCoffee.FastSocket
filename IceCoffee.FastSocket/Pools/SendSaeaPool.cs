using IceCoffee.Common.Pools;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    internal class SendSaeaPool : ConcurrentBagPool<SocketAsyncEventArgs>
    {
        private readonly EventHandler<SocketAsyncEventArgs> _asyncCompletedEventHandler;

        public SendSaeaPool(EventHandler<SocketAsyncEventArgs> asyncCompletedEventHandler)
        {
            this._asyncCompletedEventHandler = asyncCompletedEventHandler;
        }

        protected override SocketAsyncEventArgs Create()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += _asyncCompletedEventHandler;
            return saea;
        }
    }
}
