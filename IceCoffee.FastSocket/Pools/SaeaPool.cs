using IceCoffee.Common.Pools;
using IceCoffee.FastSocket.Tcp;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Net.Sockets;

namespace IceCoffee.FastSocket.Pools
{
    /// <summary>
    /// 普通 Saea 缓存 CPU*2 个即足够 
    /// </summary>
    internal class SaeaPool : LocklessPool<SocketAsyncEventArgs>
    {
        public SaeaPool(Func<SocketAsyncEventArgs> objectGenerator) : base(objectGenerator)
        {
        }
    }
}
