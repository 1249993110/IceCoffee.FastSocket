using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IceCoffee.FastSocket.Pools;

namespace IceCoffee.FastSocket.Tcp
{
    /// <summary>
    /// TCP服务器-用于连接、断开和管理 TCP 会话 
    /// </summary>
    /// <remarks>Thread-safe</remarks>
    public class TcpServer : IDisposable
    {
        #region 字段
        private readonly Guid _id;
        private bool _isListening;
        private IPEndPoint _endPoint;
        private readonly TcpServerOptions _options;

        private readonly ConcurrentDictionary<int, TcpSession> _sessions;
        private SocketAsyncEventArgs _acceptEventArg;
        private Socket _socketAcceptor;
        private TcpSessionPool _sessionPool;
        private SaeaPool _recvSaeaPool;
        private SaeaPool _sendSaeaPool;
        #endregion

        #region 属性
        /// <summary>
        /// 服务端 Id
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// 是否正在侦听
        /// </summary>
        public bool IsListening => _isListening;

        /// <summary>
        /// IP 终结点
        /// </summary>
        public IPEndPoint EndPoint => _endPoint;

        /// <summary>
        /// Tcp 服务端选项
        /// </summary>
        public TcpServerOptions Options => _options;

        /// <summary>
        /// 会话
        /// </summary>
        public IReadOnlyDictionary<int, TcpSession> Sessions => _sessions;

        /// <summary>
        /// 连接到服务器的会话数
        /// </summary>
        public int SessionCount { get { return _sessions.Count; } }
        #endregion

        #region 方法

        #region 构造方法
        /// <summary>
        /// 使用给定的 IP 地址和端口号初始化 TCP 服务器
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Tcp server options</param>
        public TcpServer(IPAddress address, int port, TcpServerOptions options = null)
            : this(new IPEndPoint(address, port), options)
        {
        }

        /// <summary>
        /// 使用给定的 IP 地址和端口号初始化 TCP 服务器
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public TcpServer(string address, int port, TcpServerOptions options = null)
            : this(new IPEndPoint(IPAddress.Parse(address), port), options)
        {
        }

        /// <summary>
        /// 使用给定的 IP 端点初始化 TCP 服务器
        /// </summary>
        /// <param name="endPoint">IP end point</param>
        public TcpServer(IPEndPoint endPoint, TcpServerOptions options = null)
        {
            _id = Guid.NewGuid();
            _endPoint = endPoint;
            _options = options ?? new TcpServerOptions();
            _sessions = new ConcurrentDictionary<int, TcpSession>();
            
        }
        #endregion

        #region 私有方法
        #region Accepting clients
        /// <summary>
        /// 开始接受新的客户端连接
        /// </summary>
        private void StartAccept()
        {
            // 由于正在重用上下文对象，因此必须清除套接字
            _acceptEventArg.AcceptSocket = null;

            // 异步接受新的客户端连接
            if (_socketAcceptor.AcceptAsync(_acceptEventArg) == false)
            {
                ProcessAccept();
            }
        }

        /// <summary>
        /// 处理接受的客户端连接
        /// </summary>
        private void ProcessAccept()
        {
            Socket socket = _acceptEventArg.AcceptSocket;
            SocketError socketError = _acceptEventArg.SocketError;

            // 会话未开始，只需 ShutdownAndClose
            if (_isListening == false)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                finally
                {
                    socket.Close();
                }

                return;
            }

            // 接受下一个客户端连接
            Task.Run(StartAccept);

            TcpSession session = null;
            SocketAsyncEventArgs receiveSaea = null;
            try
            {
                if (socketError != SocketError.Success)
                {
                    throw new SocketException("异常 socket 连接", socketError);
                }
                else
                {
                    int sessionId = socket.Handle.ToInt32();
                    session = _sessionPool.Take();
                    session.Initialize(socket, this, sessionId);

                    if(_sessions.TryAdd(sessionId, session) == false)
                    {
                        throw new SocketException($"添加会话错误，sessionId: {sessionId} 已存在");
                    }

                    OnSessionStarted(session);

                    receiveSaea = _recvSaeaPool.Take();
                    receiveSaea.UserToken = session;
                    if (socket.ReceiveAsync(receiveSaea) == false)
                    {
                        ProcessReceive(receiveSaea);
                    }
                }
            }
            catch (SocketException ex)
            {
                ProcessClose(receiveSaea);
                RaiseException(ex);
            }
            catch (Exception ex)
            {
                ProcessClose(receiveSaea);
                RaiseException(new SocketException("Error in ProcessAccept", ex));
            }
        }

        /// <summary>
        /// 该方法是与 Socket.AcceptAsync() 关联的回调方法，操作并在接受操作完成时调用 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAcceptAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept();
        }
        #endregion

        #region Recv data from clients
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            TcpSession session = e.UserToken as TcpSession;
            SocketError socketError = e.SocketError;

            try
            {
                if (e.BytesTransferred > 0 && socketError == SocketError.Success)
                {
                    session.ReadBuffer.CacheSaea(e);
                    session.OnReceived();

                    SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Take();
                    receiveSaea.UserToken = session;

                    if (session._socket.ReceiveAsync(receiveSaea) == false)
                    {
                        ProcessReceive(receiveSaea);
                    }
                }
                else
                {
                    throw new SocketException("异常 socket 连接", socketError);
                }
            }
            catch(SocketException ex)
            {
                ProcessClose(e);
                RaiseException(ex);
            }
            catch (Exception ex)
            {
                ProcessClose(e);
                string errorMsg = $"Error in ProcessReceive，会话Id: {session.SessionId}, IPEndPoint: {session.RemoteIPEndPoint} 即将关闭";
                RaiseException(new SocketException(errorMsg, ex));
            }
        }
        private void OnRecvAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }
        #endregion

        #region Send data to clients
        public void SendAsync(TcpSession session, byte[] data, int offset, int count)
        {
            if(count <= 0)
            {
                return;
            }

            Socket socket = session._socket;
            SocketAsyncEventArgs sendSaea = _sendSaeaPool.Take();
            sendSaea.UserToken = session;

            int sendCount = count;
            int sendBufferSize = _options.SendBufferSize;
            if (count > sendBufferSize)// 如果 count 大于发送缓冲区大小
            {
                sendCount = sendBufferSize;
                Array.Copy(data, offset, sendSaea.Buffer, 0, sendCount);
                sendSaea.SetBuffer(0, sendCount);
                if (socket.SendAsync(sendSaea) == false)
                {
                    ProcessSend(sendSaea);
                }

                SendAsync(session, data, offset + sendCount, count - sendCount);
            }
            else
            {
                Array.Copy(data, offset, sendSaea.Buffer, 0, sendCount);
                sendSaea.SetBuffer(0, sendCount);
                if (socket.SendAsync(sendSaea) == false)
                {
                    ProcessSend(sendSaea);
                }
            }
        }
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                ProcessClose(e);
            }
        }
        private void OnSendAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }
        #endregion
        
        /// <summary>
        /// 处理关闭
        /// </summary>
        /// <param name="e"></param>
        private void ProcessClose(SocketAsyncEventArgs e)
        {
            TcpSession session = e.UserToken as TcpSession;
            
            if (_isListening)
            {
                if (session != null)
                {
                    _sessions.TryRemove(session.SessionId, out _);

                    session.Close();
                    OnSessionClosed(session);
                    _sessionPool.Put(session);
                }

                if(e != null)
                {
                    switch (e.LastOperation)
                    {
                        case SocketAsyncOperation.Receive:
                            _recvSaeaPool.Put(e);
                            break;
                        case SocketAsyncOperation.Send:
                            _sendSaeaPool.Put(e);
                            break;
                        default:
                            e.Dispose();
                            throw new SocketException("套接字上完成的最后一个操作不是接收或发送");
                    }
                }
            }
            else
            {
                session?.Close();
                e?.Dispose();
            }
        }

        /// <summary>
        /// 引发异常
        /// </summary>
        private void RaiseException(SocketException socketException)
        {
            SocketError error = socketException.SocketError;
            //跳过断开连接错误 
            if (error == SocketError.ConnectionAborted
                || error == SocketError.ConnectionRefused
                || error == SocketError.ConnectionReset
                || error == SocketError.OperationAborted
                || error == SocketError.Shutdown)
            {
                return;
            }

            OnException(socketException);
        }
        #endregion

        #region 保护方法
        protected virtual void OnException(SocketException socketException) {  }
        
        /// <summary>
        /// 创建一个新的套接字接受器对象
        /// </summary>
        /// <remarks>
        /// 如果您需要在您的实现中准备一些特定的套接字对象，则方法可能会被覆盖
        /// </remarks>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocketAcceptor()
        {
            var socket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            
            // 应用选项：使用 keep-alive
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.KeepAlive);
            // 应用选项：不延迟直接发送
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, _options.NoDelay);
            // 应用选项：重用地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _options.ReuseAddress);
            // 应用选项：独占地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, _options.ExclusiveAddressUse);

            // 应用选项：双模式（必须在侦听前应用此选项） 
            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = _options.DualMode;
            }

            return socket;
        }

        /// <summary>
        /// 创建会话
        /// </summary>
        /// <returns></returns>
        internal protected virtual TcpSession CreateSession()
        {
            return new TcpSession();
        }

        /// <summary>
        /// 当服务端开始侦听后调用
        /// </summary>
        protected virtual void OnStarted() { }

        /// <summary>
        /// 当服务端停止监听后调用
        /// </summary>
        protected virtual void OnStopped() { }

        /// <summary>
        /// 当新会话开始后调用
        /// </summary>
        /// <param name="session">Connected session</param>
        protected virtual void OnSessionStarted(TcpSession session) { }

        /// <summary>
        /// 当会话关闭后调用
        /// </summary>
        /// <param name="session">Disconnected session</param>
        protected virtual void OnSessionClosed(TcpSession session) { }
        #endregion

        #region 公开方法
        /// <summary>
        /// 启动服务器
        /// </summary>
        /// <returns>'true' 如果服务器启动成功, 'false' 如果服务器启动失败</returns>
        public virtual bool Start()
        {
            if (_isListening)
            {
                return false;
            }

            _sessionPool = new TcpSessionPool(CreateSession);
            _recvSaeaPool = new SaeaPool(OnRecvAsyncCompleted, _options.ReceiveBufferSize);
            _sendSaeaPool = new SaeaPool(OnSendAsyncCompleted, _options.SendBufferSize);

            // 设置接受者事件参数
            _acceptEventArg = new SocketAsyncEventArgs();
            _acceptEventArg.Completed += OnAcceptAsyncCompleted;

            // 创建一个新的套接字接受器
            _socketAcceptor = CreateSocketAcceptor();
            // 将接受者套接字绑定到 IP 终结点
            _socketAcceptor.Bind(_endPoint);
            // 根据创建的实际端点刷新端点属性
            _endPoint = (IPEndPoint)_socketAcceptor.LocalEndPoint;
            // 开始侦听具有给定操作系统 TCP 缓存大小的接受者套接字
            _socketAcceptor.Listen(_options.AcceptorBacklog);

            _isListening = true;
            OnStarted();

            // 开始接受客户端
            StartAccept();

            return true;
        }

        public virtual bool Restart()
        {
            Stop();
            return Start();
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public virtual bool Stop()
        {
            if (_isListening == false)
            {
                return false;
            }

            _isListening = false;

            foreach (var item in _sessions)
            {
                var session = item.Value;
                session.Dispose();
                OnSessionClosed(session);
            }

            _sessions.Clear();

            _recvSaeaPool.Dispose();
            _recvSaeaPool = null;
            _sendSaeaPool.Dispose();
            _sendSaeaPool = null;
            _sessionPool.Dispose();
            _sessionPool = null;
            _acceptEventArg.Dispose();
            _acceptEventArg = null;
            _socketAcceptor.Dispose();
            _socketAcceptor = null;

            OnStopped();

            return true;
        }

        /// <summary>
        /// 向所有连接的客户端组播数据
        /// </summary>
        /// <param name="buffer">Buffer to multicast</param>
        /// <param name="offset">Buffer offset</param>
        /// <param name="size">Buffer size</param>
        /// <returns>'true' if the data was successfully multicasted, 'false' if the data was not multicasted</returns>
        public virtual bool Multicast(byte[] buffer, int offset, int size)
        {
            if (_isListening == false || size <= 0)
            {
                return false;
            }

            // Multicast data to all sessions
            foreach (var session in Sessions.Values)
            {
                session.SendAsync(buffer, offset, size);
            }

            return true;
        }
        
        /// <summary>
        /// 向所有连接的客户端组播数据
        /// </summary>
        /// <param name="buffer">Buffer to multicast</param>
        /// <returns>'true' if the data was successfully multicasted, 'false' if the data was not multicasted</returns>
        public virtual bool Multicast(byte[] buffer)
        {
            return Multicast(buffer, 0, buffer.Length);
        }
        #endregion

        #region 内部方法
        internal void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            if (_isListening)
            {
                _recvSaeaPool.Put(e);
            }
            else
            {
                e.Dispose();
            }
        }
        #endregion

        #region IDisposable implementation
        private bool _isDisposed;

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
                    Stop();
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _isDisposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~TcpServer()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion
        #endregion

    }
}
