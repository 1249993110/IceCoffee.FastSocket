using IceCoffee.Common.Pools;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    internal class ReceiveSaeaPool : ConnectionPool<SocketAsyncEventArgs>
    {
        private readonly EventHandler<SocketAsyncEventArgs> _asyncCompletedEventHandler;

        private readonly int _bufferSize;

        public ReceiveSaeaPool(EventHandler<SocketAsyncEventArgs> asyncCompletedEventHandler, int bufferSize)
        {
            this._asyncCompletedEventHandler = asyncCompletedEventHandler;
            this._bufferSize = bufferSize;

            base.Min = Environment.ProcessorCount;
            if (base.Min < 2)
            {
                base.Min = 2;
            }

            base.Max = int.MaxValue;

            base.IdleTime = 6;
            base.AllIdleTime = 60;
        }

        protected override SocketAsyncEventArgs Create()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += _asyncCompletedEventHandler;
            saea.SetBuffer(new byte[_bufferSize], 0, _bufferSize);
            return saea;
        }
    }
}