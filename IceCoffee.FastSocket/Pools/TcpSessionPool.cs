using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;

namespace IceCoffee.FastSocket.Pools
{
    internal class TcpSessionPool : ConnectionPool<TcpSession>
    {
        private readonly Func<TcpSession> _createSessionFunc;

        public TcpSessionPool(Func<TcpSession> createSessionFunc)
        {
            _createSessionFunc = createSessionFunc;

            Min = Environment.ProcessorCount;
            if (Min < 2)
            {
                Min = 2;
            }

            Max = int.MaxValue;

            IdleTime = 60;
            AllIdleTime = 600;
        }

        protected override TcpSession Create()
        {
            return _createSessionFunc.Invoke();
        }
    }
}