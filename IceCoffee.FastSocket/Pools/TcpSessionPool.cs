using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;

namespace IceCoffee.FastSocket.Pools
{
    internal class TcpSessionPool : ConcurrentBagPool<TcpSession>
    {
        private readonly Func<TcpSession> _createCallback;

        public TcpSessionPool(Func<TcpSession> createCallback)
        {
            _createCallback = createCallback;
        }

        protected override TcpSession Create()
        {
            return _createCallback.Invoke();
        }
    }
}