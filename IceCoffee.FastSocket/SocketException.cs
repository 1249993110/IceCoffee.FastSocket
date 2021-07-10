using IceCoffee.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket
{
    /// <summary>
    /// 套接字异常
    /// </summary>
    public class SocketException : CustomExceptionBase
    {
        /// <summary>
        /// 套接字错误
        /// </summary>
        public SocketError SocketError { get; set; }

        /// <summary>
        /// 实例化 SocketException
        /// </summary>
        /// <param name="message"></param>
        public SocketException(string message) : base(message) { }

        /// <summary>
        /// 实例化 SocketException
        /// </summary>
        /// <param name="message"></param>
        /// <param name="socketError"></param>
        public SocketException(string message, SocketError socketError) : base(message)
        {
            SocketError = socketError;
        }

        /// <summary>
        /// 实例化 SocketException
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public SocketException(string message, Exception innerException) : base(message, innerException) { }
    }
}
