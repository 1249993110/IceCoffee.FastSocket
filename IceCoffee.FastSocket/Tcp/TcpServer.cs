using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        private IPEndPoint _endPoint;
        private bool _isDisposed;
        private bool _isListening;

        //private readonly ManualResetEvent _acceptorEvent;
        private readonly ConcurrentDictionary<int, TcpSession> _sessions;
        private SocketAsyncEventArgs _acceptorEventArg;
        private Socket _socketAcceptor;
        private readonly TcpServerOptions _options;
        #endregion

        #region 属性
        /// <summary>
        /// Server Id
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// IP 终结点
        /// </summary>
        public IPEndPoint EndPoint => _endPoint;

        /// <summary>
        /// 是否正在侦听
        /// </summary>
        public bool IsListening => _isListening;

        /// <summary>
        /// TcpServer 选项
        /// </summary>
        public TcpServerOptions Options => _options;

        /// <summary>
        /// 是否是服务端
        /// </summary>
        public bool IsServer => true;

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
            _acceptorEventArg.AcceptSocket = null;

            // 异步接受新的客户端连接
            if (_socketAcceptor.AcceptAsync(_acceptorEventArg) == false)
            {
                ProcessAccept();
            }
        }

        /// <summary>
        /// 处理接受的客户端连接
        /// </summary>
        private void ProcessAccept()
        {
            Socket socket = _acceptorEventArg.AcceptSocket;
            SocketError socketError = _acceptorEventArg.SocketError;

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

            try
            {
                if (socketError != SocketError.Success)
                {
                    throw new SocketException("异常 socket 连接", socketError);
                }
                else
                {
                    int sessionId = socket.Handle.ToInt32();

                    SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Take();

                    TcpSession session = _sessionPool.Take();

                    session.Attach(socket, sessionId);
                    receiveSaea.UserToken = session;

                    if(_sessions.TryAdd(sessionId, session) == false)
                    {
                        
                        throw new SocketException($"添加会话错误，sessionId: {sessionId} 已存在");
                    }

                    OnConnected(session);
                    if (session.socket.ReceiveAsync(receiveSaea) == false)
                    {
                        Task.Run(() =>
                        {
                            OnRecvAsyncRequestCompleted(session.socket, receiveSaea);
                        });
                    }
                }
            }
            catch (SocketException ex)
            {
                //OnInternalClose(session, CloseReason.ApplicationError);
                RaiseException(ex);
            }
            catch (Exception ex)
            {
                //OnInternalClose(session, CloseReason.ApplicationError);
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

        /// <summary>
        /// 引发异常
        /// </summary>
        private void RaiseException(SocketException socketException)
        {
            OnException(socketException);
        }
        #endregion
        #endregion

        #region 保护方法
        protected virtual void OnException(SocketException socketException)
        {
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
            // 应用选项：重用地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _options.ReuseAddress);
            // 应用选项：独占地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, _options.ExclusiveAddressUse);
            // 应用选项：使用 keep-alive
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.KeepAlive);
            // 应用选项：双模式（必须在侦听前应用此选项） 
            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = _options.DualMode;
            }

            return socket;
        }

        /// <summary>
        ///  Create TCP session factory method
        /// </summary>
        /// <returns></returns>
        protected virtual TcpSession CreateSession()
        {
            return new TcpSession(this);
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
        /// 当新会话建立时调用
        /// </summary>
        /// <param name="session">Connected session</param>
        protected virtual void OnSessionSetup(TcpSession session) { }

        /// <summary>
        /// 当会话关闭时调用
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

            // 设置接受者事件参数
            _acceptorEventArg = new SocketAsyncEventArgs();
            _acceptorEventArg.Completed += OnAcceptAsyncCompleted;

            // 创建一个新的套接字接受器
            _socketAcceptor = CreateSocketAcceptor();

            // 将接受者套接字绑定到 IP 终结点
            _socketAcceptor.Bind(_endPoint);
            // 根据创建的实际端点刷新端点属性
            _endPoint = (IPEndPoint)_socketAcceptor.LocalEndPoint;

            // 开始侦听具有给定接受积压大小的接受者套接字
            _socketAcceptor.Listen(_options.AcceptorBacklog);

            // Reset field
            // _bytesPending = 0;
            // _bytesSent = 0;
            // _bytesReceived = 0;

            _isListening = true;
            OnStarted();

            // 开始接受客户端
            StartAccept();

            return true;
        }

        public virtual void Stop()
        {
        }
        #endregion

        #region 其他方法

        #endregion

        #endregion

        #region IDisposable implementation
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
    }
}
