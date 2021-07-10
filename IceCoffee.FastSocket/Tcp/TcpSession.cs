using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    public class TcpSession
    {
        #region 字段

        private readonly TcpServer _tcpServer;

        private Socket _socket;

        private int _sessionId;

        private DateTime _connectTime;

        #endregion

        public TcpSession(TcpServer tcpServer)
        {
            _tcpServer = tcpServer;
        }

        /// <summary>
        /// 为会话附加信息
        /// </summary>
        /// <param name="socket"></param>
        internal void Attach(Socket socket)
        {
            this._socket = socket;
            this._sessionId = socket.Handle.ToInt32();
            this._connectTime = DateTime.Now;

            //if (_keepAlive.Enable)
            //{
            //    socket.IOControl(IOControlCode.KeepAliveValues, _keepAlive.GetKeepAliveData(), null);
            //}
        }

        /// <summary>
        /// 清除会话附加信息
        /// </summary>
        internal void Detach()
        {
            this._socket = null;
            this._sessionId = 0;
            this._connectTime = default;
        }
    }
}
