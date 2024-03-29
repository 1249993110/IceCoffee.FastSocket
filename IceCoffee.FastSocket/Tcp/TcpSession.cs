﻿using System;
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
        internal Socket socket;
        private readonly TcpServer _tcpServer;
        private int _sessionId;
        private DateTime _connectedTime;
        private IPEndPoint _remoteEndPoint;

        protected internal readonly ReadBuffer ReadBuffer;
        #endregion

        #region 属性
        public TcpServer Server => _tcpServer;

        public int SessionId => _sessionId;

        public DateTime ConnectedTime => _connectedTime;

        public IPEndPoint RemoteIPEndPoint => _remoteEndPoint;
        #endregion 属性

        public TcpSession(TcpServer tcpServer)
        {
            _tcpServer = tcpServer;
            ReadBuffer = new ReadBuffer(_tcpServer.CollectRecvSaea, _tcpServer.Options.ReceiveBufferSize);
        }

        /// <summary>
        /// 初始化
        /// </summary>
        internal void Initialize(Socket socket,  int sessionId)
        {
            this.socket = socket;
            _sessionId = sessionId;
            _connectedTime = DateTime.Now;
            _remoteEndPoint = socket.RemoteEndPoint as IPEndPoint;
            OnStarted();
        }

        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public virtual void SendAsync(byte[] buffer) 
        {
            _tcpServer.SendAsync(this, buffer); 
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
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="bufferList"></param>
        public virtual void SendAsync(IList<ArraySegment<byte>> bufferList)
        {
            _tcpServer.SendAsync(this, bufferList);
        }

        /// <summary>
        /// 关闭会话
        /// </summary>
        public virtual void Close()
        {
            lock (this)
            {
                if (socket != null)
                {
                    try
                    {
                        socket.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException)
                    {
                    }

                    socket.Close();
                    ReadBuffer.Clear();
                    OnClosed();

                    socket = null;
                }
            }
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
        protected internal virtual void OnReceived() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Close();
            }
        }
    }
}
