using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;

namespace IceCoffee.FastSocket.Pools
{
    internal class TcpSessionPool : ConcurrentBagPool<TcpSession>
    {
        private readonly Func<TcpSession> _createSessionFunc;

        public TcpSessionPool(Func<TcpSession> createSessionFunc)
        {
            _createSessionFunc = createSessionFunc;
        }

        protected override TcpSession Create()
        {
            return _createSessionFunc.Invoke();
        }
    }
}