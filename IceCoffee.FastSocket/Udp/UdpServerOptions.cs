using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Udp
{
    /// <summary>
    /// Udp 服务端选项
    /// </summary>
    public class UdpServerOptions
    {
        /// <summary>
        /// 选项：接收缓冲区大小
        /// <remarks>默认值是 1024</remarks>
        /// </summary>
        [DefaultValue(1024)]
        public int ReceiveBufferSize { get; set; } = 1024;

        /// <summary>
        /// 选项：重用地址
        /// </summary>
        /// <remarks>
        /// 如果操作系统支持此功能, 此选项将启用/禁用 SO_REUSEADDR
        /// </remarks>
        public bool ReuseAddress { get; set; }

        /// <summary>
        /// 选项：使套接字绑定为独占访问 
        /// </summary>
        /// <remarks>
        /// 如果操作系统支持此功能, 此选项将启用/禁用 SO_EXCLUSIVEADDRUSE
        /// </remarks>
        public bool ExclusiveAddressUse { get; set; }

        /// <summary>
        /// 选项：双模式
        /// </summary>
        /// <remarks>
        /// 指定 Socket 是否是用于 IPv4 和 IPv6 的双模式套接字, 仅当套接字绑定在 IPv6 地址上时才有效
        /// </remarks>
        public bool DualMode { get; set; }
    }
}
