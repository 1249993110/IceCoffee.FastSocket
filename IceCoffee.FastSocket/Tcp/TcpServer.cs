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
        private volatile bool _isListening;
        private IPEndPoint _endPoint;
        private readonly TcpServerOptions _options;

        private readonly ConcurrentDictionary<int, TcpSession> _sessions;
       
        private SocketAsyncEventArgs _acceptEventArg;
        private Socket _socketAcceptor;
        private TcpSessionPool _sessionPool;
        private TcpReceiveSaeaPool _recvSaeaPool;
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
        /// 已打开的会话
        /// </summary>
        public IReadOnlyDictionary<int, TcpSession> Sessions => _sessions;

        /// <summary>
        /// 连接到服务器的会话数
        /// </summary>
        public int SessionCount => _sessions.Count;
        #endregion

        #region 事件
        public event Action Started;
        public event Action Stopped;
        public event Action<TcpSession> SessionStarted;
        public event Action<TcpSession> SessionClosed;
        public event Action<Exception> ExceptionCaught;
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
        /// <param name="options"></param>
        public TcpServer(string address, int port, TcpServerOptions options = null)
            : this(new IPEndPoint(IPAddress.Parse(address), port), options)
        {
        }

        /// <summary>
        /// 使用给定的 IP 端点初始化 TCP 服务器
        /// </summary>
        /// <param name="endPoint">IP end point</param>
        /// <param name="options"></param>
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
            try
            {
                // 由于正在重用上下文对象，因此必须清除套接字
                _acceptEventArg.AcceptSocket = null;

                // 异步接受新的客户端连接
                if (_socketAcceptor.AcceptAsync(_acceptEventArg) == false)
                {
                    ProcessAccept();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        /// <summary>
        /// 处理接受的客户端连接
        /// </summary>
        private void ProcessAccept()
        {
            try
            {
                if (_isListening == false)
                {
                    return;
                }

                Socket socket = _acceptEventArg.AcceptSocket;
                SocketError socketError = _acceptEventArg.SocketError;

                // 接受下一个客户端连接
                Task.Run(StartAccept);

                TcpSession session = null;
                SocketAsyncEventArgs receiveSaea = null;
                try
                {
                    if (socketError != SocketError.Success)
                    {
                        throw new SocketException((int)socketError);
                    }
                    else
                    {
                        int sessionId = socket.Handle.ToInt32();
                        session = _sessionPool.Get();
                        session.Initialize(socket, sessionId);

                        if (_sessions.TryAdd(sessionId, session) == false)
                        {
                            throw new Exception($"添加会话错误，sessionId: {sessionId} 已存在");
                        }

                        OnSessionStarted(session);

                        receiveSaea = _recvSaeaPool.Get();
                        receiveSaea.UserToken = session;
                        if (socket.ReceiveAsync(receiveSaea) == false)
                        {
                            ProcessReceive(receiveSaea);
                        }
                    }
                }
                catch (Exception ex)
                {
                    RaiseException(ex);
                    ProcessClose(receiveSaea);
                    CollectSession(session);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
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
            try
            {
                TcpSession session = (TcpSession)e.UserToken;
                SocketError socketError = e.SocketError;

                if (socketError != SocketError.Success)
                {
                    throw new SocketException((int)socketError);
                }
                else
                {
                    // If zero is returned from a read operation, the remote end has closed the connection
                    if (e.BytesTransferred == 0)
                    {
                        ProcessClose(e);
                        return;
                    }
                    
                    session.ReadBuffer.CacheSaea(e);

                    // 如果在接收数据中出现异常，则不需要回收saea 只需回收会话，因为在 ReadBuffer.Clear 中已经回收过 saea
                    try
                    {
                        session.OnReceived();
                    }
                    catch (Exception ex)
                    {
                        RaiseException(ex);
                        CollectSession(session);
                        return;
                    }

                    // 如果在接收数据中关闭会话，则不需要回收saea 只需回收会话，因为在 ReadBuffer.Clear 中已经回收过 saea
                    if (session.socket == null)
                    {
                        CollectSession(session);
                        return;
                    }

                    e = _recvSaeaPool.Get();
                    e.UserToken = session;
                    if (session.socket.ReceiveAsync(e) == false)
                    {
                        ProcessReceive(e);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(e);
            }
        }
        private void OnRecvAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessReceive(e);
        }
        #endregion

        #region Send data to clients
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            try
            {
                if (e.SocketError != SocketError.Success)
                {
                    throw new SocketException((int)e.SocketError);
                }
                else
                {
                    CollectSendSaea(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(e);
            }
        }
        private void OnSendAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            ProcessSend(e);
        }
        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public virtual void SendAsync(TcpSession session, byte[] buffer, int offset, int count)
        {
            try
            {
                if (session == null)
                {
                    throw new ArgumentNullException(nameof(session));
                }

                if (buffer == null)
                {
                    throw new ArgumentNullException(nameof(buffer));
                }

                if (_isListening == false)
                {
                    throw new Exception("服务端已经停止监听");
                }

                var e = _sendSaeaPool.Get();
                e.UserToken = session;
                e.SetBuffer(buffer, offset, count);

                if (session.socket.SendAsync(e) == false)
                {
                    ProcessSend(e);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error in TcpServer.SendAsync", ex);
            }
        }
        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="buffer"></param>
        public virtual void SendAsync(TcpSession session, byte[] buffer)
        {
            SendAsync(session, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="session"></param>
        /// <param name="bufferList"></param>
        public virtual void SendAsync(TcpSession session, IList<ArraySegment<byte>> bufferList)
        {
            try
            {
                if (session == null)
                {
                    throw new ArgumentNullException(nameof(session));
                }

                if (bufferList == null || bufferList.Count <= 0)
                {
                    throw new ArgumentNullException(nameof(bufferList));
                }

                if (_isListening == false)
                {
                    throw new Exception("服务端已经停止监听");
                }

                var e = _sendSaeaPool.Get();
                e.UserToken = session;
                e.BufferList = bufferList;

                if (session.socket.SendAsync(e) == false)
                {
                    ProcessSend(e);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error in TcpServer.SendAsync", ex);
            }
        }
        #endregion

        /// <summary>
        /// 处理关闭
        /// </summary>
        /// <param name="e"></param>
        private void ProcessClose(SocketAsyncEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }

                if (e.UserToken is TcpSession session)
                {
                    CollectSession(session);
                }

                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Receive:
                        CollectRecvSaea(e);
                        break;
                    case SocketAsyncOperation.Send:
                        CollectSendSaea(e);
                        break;
                    default:
                        e.Dispose();
                        throw new ArgumentException("套接字上完成的最后一个操作不是接收或发送");
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        /// <summary>
        /// 引发异常
        /// </summary>
        private void RaiseException(Exception exception)
        {
            if(exception is SocketException socketException)
            {
                SocketError error = socketException.SocketErrorCode;
                //跳过断开连接错误 
                if (error == SocketError.ConnectionAborted
                    || error == SocketError.ConnectionRefused
                    || error == SocketError.ConnectionReset
                    || error == SocketError.OperationAborted
                    || error == SocketError.Shutdown)
                {
                    return;
                }
            }

            OnExceptionCaught(exception);
        }

        private SocketAsyncEventArgs CreateRecvSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += OnRecvAsyncCompleted;
            saea.SetBuffer(new byte[_options.ReceiveBufferSize], 0, _options.ReceiveBufferSize);
            return saea;
        }
        private SocketAsyncEventArgs CreateSendSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += OnSendAsyncCompleted;
            return saea;
        }
        #endregion

        #region 保护方法
        /// <summary>
        /// 当捕获到非检查异常时调用
        /// </summary>
        /// <param name="exception"></param>
        protected virtual void OnExceptionCaught(Exception exception) 
        {
            ExceptionCaught?.Invoke(exception);
        }
        
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
        protected virtual TcpSession CreateSession()
        {
            return new TcpSession(this);
        }

        /// <summary>
        /// 当服务端开始侦听后调用
        /// </summary>
        protected virtual void OnStarted() 
        {
            Started?.Invoke();
        }

        /// <summary>
        /// 当服务端停止监听后调用
        /// </summary>
        protected virtual void OnStopped() 
        {
            Stopped?.Invoke();
        }

        /// <summary>
        /// 当新会话开始后调用
        /// </summary>
        /// <param name="session">Connected session</param>
        protected virtual void OnSessionStarted(TcpSession session) 
        {
            SessionStarted?.Invoke(session);
        }

        /// <summary>
        /// 当会话关闭后调用
        /// </summary>
        /// <param name="session">Disconnected session</param>
        protected virtual void OnSessionClosed(TcpSession session) 
        {
            SessionClosed?.Invoke(session);
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 启动服务器
        /// </summary>
        public virtual void Start()
        {
            try
            {
                if (_isListening)
                {
                    throw new Exception("服务端已经开始监听");
                }

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

                _sessionPool = new TcpSessionPool(CreateSession);
                _recvSaeaPool = new TcpReceiveSaeaPool(CreateRecvSaea);
                _sendSaeaPool = new SaeaPool(CreateSendSaea);

                _isListening = true;
                OnStarted();

                // 开始接受客户端
                StartAccept();
            }
            catch (Exception ex)
            {
                throw new Exception("Error in TcpServer.Start", ex);
            }
        }

        /// <summary>
        /// 重新启动服务器
        /// </summary>
        /// <returns></returns>
        public virtual void Restart()
        {
            if (_isListening)
            {
                Stop();
            }
            
            Start();
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public virtual void Stop()
        {
            try
            {
                if (_isListening == false)
                {
                    throw new Exception("服务端已经停止监听");
                }

                _isListening = false;

                foreach (var item in _sessions)
                {
                    var session = item.Value;
                    session.Dispose();
                    OnSessionClosed(session);
                }

                _sessions.Clear();

                _sessionPool.Dispose();
                _sessionPool = null;
                _recvSaeaPool.Dispose();
                _recvSaeaPool = null;
                _sendSaeaPool.Dispose();
                _sendSaeaPool = null;
                _acceptEventArg.Dispose();
                _acceptEventArg = null;
                _socketAcceptor.Dispose();
                _socketAcceptor = null;

                OnStopped();
            }
            catch (Exception ex)
            {
                throw new Exception("Error in TcpServer.Stop", ex);
            }
        }

        #region 组播
        /// <summary>
        /// 向所有连接的客户端组播数据
        /// </summary>
        /// <param name="buffer">Buffer to multicast</param>
        /// <param name="offset">Buffer offset</param>
        /// <param name="size">Buffer size</param>
        public virtual void Multicast(byte[] buffer, int offset, int size)
        {
            // Multicast data to all sessions
            foreach (var session in Sessions.Values)
            {
                SendAsync(session, buffer, offset, size);
            }
        }

        /// <summary>
        /// 向所有连接的客户端组播数据
        /// </summary>
        /// <param name="buffer">Buffer to multicast</param>
        public virtual void Multicast(byte[] buffer)
        {
            Multicast(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 向所有连接的客户端组播数据
        /// </summary>
        /// <param name="bufferList"></param>
        public virtual void Multicast(IList<ArraySegment<byte>> bufferList)
        {
            // Multicast data to all sessions
            foreach (var session in Sessions.Values)
            {
                SendAsync(session, bufferList);
            }
        }
        #endregion

        #endregion

        #region 回收资源
        internal void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            try
            {
                if (_isListening)
                {
                    e.UserToken = null;
                    _recvSaeaPool.Return(e);
                }
                else
                {
                    e.Dispose();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        private void CollectSendSaea(SocketAsyncEventArgs e)
        {
            try
            {
                if (_isListening)
                {
                    e.SetBuffer(null, 0, 0);
                    e.BufferList = null;
                    e.UserToken = null;
                    _sendSaeaPool.Return(e);
                }
                else
                {
                    e.Dispose();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        private void CollectSession(TcpSession session)
        {
            try
            {
                if(session == null)
                {
                    return;
                }

                if (_isListening)
                {
                    if(_sessions.TryRemove(session.SessionId, out _))
                    {
                        session.Close();
                        OnSessionClosed(session);
                        _sessionPool.Return(session);
                        return;
                    }
                }
                
                session.Dispose();
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        #endregion

        #region IDisposable implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
            }
        }
        #endregion
        #endregion

    }
}
