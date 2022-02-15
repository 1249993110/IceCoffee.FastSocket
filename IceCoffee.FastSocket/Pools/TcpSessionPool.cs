using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;

namespace IceCoffee.FastSocket.Pools
{
    internal class TcpSessionPool : ConcurrentBagPool<TcpSession>
    {
        public TcpSessionPool(Func<TcpSession> objectGenerator) : base(objectGenerator)
        {
        }
    }
}