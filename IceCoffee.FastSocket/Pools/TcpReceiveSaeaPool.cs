using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    /// <summary>
    /// Tcp 接收需要的 Saea 可无限制缓存，实际由 ReadBufferMaxLength 和 连接的会话数量限制
    /// </summary>
    internal class TcpReceiveSaeaPool : ConcurrentBagPool<SocketAsyncEventArgs>
    {
        private readonly Func<SocketAsyncEventArgs> _createCallback;

        public TcpReceiveSaeaPool(Func<SocketAsyncEventArgs> createCallback)
        {
            _createCallback = createCallback;
        }

        protected override SocketAsyncEventArgs Create()
        {
            return _createCallback.Invoke();
        }
    }
}
