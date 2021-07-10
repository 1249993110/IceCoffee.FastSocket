﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    /// <summary>
    /// TcpServer 选项
    /// </summary>
    public class TcpServerOptions
    {
        /// <summary>
        /// 选项：重用地址
        /// </summary>
        /// <remarks>
        /// 如果操作系统支持此功能，此选项将启用/禁用 SO_REUSEADDR
        /// </remarks>
        public bool ReuseAddress { get; set; }

        /// <summary>
        /// 选项：使套接字绑定为独占访问 
        /// </summary>
        /// <remarks>
        /// 如果操作系统支持此功能，此选项将启用/禁用 SO_EXCLUSIVEADDRUSE
        /// </remarks>
        public bool ExclusiveAddressUse { get; set; }

        /// <summary>
        /// 使用 keep-alive
        /// </summary>
        /// <remarks>
        /// 保持连接检测对方主机是否崩溃，避免（服务器）永远阻塞于TCP连接的输入 SO_KEEPALIVE
        /// <para>设置该选项后，如果2小时内在此套接口的任一方向都没有数据交换，TCP就自动给对方发一个存活保持探测分节 (keepalive probe)</para>
        /// </remarks>
        public bool KeepAlive { get; set; }

        /// <summary>
        /// 选项：双模式
        /// </summary>
        /// <remarks>
        /// 指定 Socket 是否是用于 IPv4 和 IPv6 的双模式套接字，仅当套接字绑定在 IPv6 地址上时才有效
        /// </remarks>
        public bool DualMode { get; set; }

        /// <summary>
        /// 选项：操作系统 TCP 缓存
        /// </summary>
        /// <remarks>
        /// 此选项将设置侦听套接字的操作系统使用 TCP 缓存 SO_Backlog
        /// <para>实际上是用于处理进站 (inbound)</para>
        /// </remarks>
        public int AcceptorBacklog { get; set; } = 1024;
    }
}
