using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    public class TcpSession : IDisposable
    {
        #region 字段
        internal Socket _socket;
        private TcpServer _tcpServer;
        private int _sessionId;
        private DateTime _connectTime;

        internal protected readonly ReadBuffer ReadBuffer;
        #endregion

        #region 属性
        public TcpServer Server => _tcpServer;

        public int SessionId => _sessionId;

        public DateTime ConnectTime => _connectTime;

        public IPEndPoint RemoteIPEndPoint => _socket.RemoteEndPoint as IPEndPoint;
        #endregion 属性

        public TcpSession()
        {
            ReadBuffer = new ReadBuffer(_tcpServer.CollectRecvSaea, _tcpServer.Options.ReceiveBufferSize);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        internal void Initialize(Socket socket, TcpServer tcpServer, int sessionId)
        {
            _socket = socket;
            _tcpServer = tcpServer;
            _sessionId = sessionId;
            _connectTime = DateTime.Now;
            OnStarted();
        }

        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public virtual void SendAsync(byte[] buffer) 
        { 
            SendAsync(buffer, 0, buffer.Length); 
        }

        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="buffer">Buffer to send</param>
        /// <param name="offset">Buffer offset</param>
        /// <param name="size">Buffer size</param>
        public virtual void SendAsync(byte[] buffer, int offset, int size)
        {
            _tcpServer.SendAsync(this, buffer, offset, size);
        }

        /// <summary>
        /// 关闭会话
        /// </summary>
        public virtual void Close()
        {
            if (_socket == null)
            {
                return;
            }

            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            finally
            {
                _socket.Close();
            }

            ReadBuffer.Clear();
            OnClosed();
        }

        /// <summary>
        /// 会话开始后调用
        /// </summary>
        protected virtual void OnStarted() { }
        /// <summary>
        /// 会话关闭后调用
        /// </summary>
        protected virtual void OnClosed() { }

        /// <summary>
        /// 收到数据后调用
        /// </summary>
        internal protected virtual void OnReceived() { }

        #region IDisposable implementation
        /// <summary>
        /// Disposed flag
        /// </summary>
        private bool _isDisposed;

        // Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            if (_isDisposed == false)
            {
                if (disposingManagedResources)
                {
                    // Dispose managed resources here...
                    Close();
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _isDisposed = true;
            }
        }

        ~TcpSession()
        {
            Dispose(false);
        }
        #endregion
    }
}
