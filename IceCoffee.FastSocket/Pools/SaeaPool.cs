using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    /// <summary>
    /// 普通 Saea 缓存 CPU*2 个即足够 
    /// </summary>
    internal class SaeaPool : LocklessPool<SocketAsyncEventArgs>
    {
        private readonly Func<SocketAsyncEventArgs> _createCallback;

        public SaeaPool(Func<SocketAsyncEventArgs> createCallback)
        {
            _createCallback = createCallback;
        }        

        protected override SocketAsyncEventArgs Create()
        {
            return _createCallback.Invoke();
        }
    }
}
