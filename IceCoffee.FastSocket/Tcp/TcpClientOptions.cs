using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    /// <summary>
    /// Tcp 客户端选项
    /// </summary>
    public class TcpClientOptions
    {
        /// <summary>
        /// 选项：接收缓冲区大小
        /// <remarks>默认值是 8192</remarks>
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 8192;

        /// <summary>
        /// 选项：发送缓冲区大小
        /// </summary>
        /// <remarks>默认值是 8192</remarks>
        public int SendBufferSize { get; set; } = 8192;

        /// <summary>
        /// 使用 keep-alive
        /// </summary>
        /// <remarks>
        /// 保持连接检测对方主机是否崩溃，避免（服务器）永远阻塞于TCP连接的输入 SO_KEEPALIVE
        /// <para>设置该选项后，如果2小时内在此套接口的任一方向都没有数据交换，TCP就自动给对方发一个存活保持探测分节 (keepalive probe)</para>
        /// </remarks>
        public bool KeepAlive { get; set; }

        /// <summary>
        /// 应用选项：不延迟直接发送
        /// </summary>
        /// <remarks>
        /// 不延迟直接发送。Tcp为了合并小包而设计，客户端默认 false，服务端默认 true
        /// </remarks>
        public bool NoDelay { get; set; } = false;
    }
}
