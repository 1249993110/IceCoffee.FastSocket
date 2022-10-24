using IceCoffee.FastSocket.Pools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    public class TcpClient : IDisposable
    {
        #region 字段
        private readonly Guid _id;
        private ConnectionState _connectionState;
        private EndPoint _remoteEndpoint;
        private readonly TcpClientOptions _options;

        protected readonly ReadBuffer ReadBuffer;

        private TcpReceiveSaeaPool _recvSaeaPool;
        private SaeaPool _sendSaeaPool;
        private SocketAsyncEventArgs _connectEventArg;
        private Socket _socketConnecter;
        #endregion

        #region 属性
        /// <summary>
        /// 客户端 Id
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// 远程终结点
        /// </summary>
        public EndPoint RemoteEndpoint => _remoteEndpoint;

        /// <summary>
        /// 本地终结点
        /// </summary>
        public EndPoint LocalEndPoint => _socketConnecter?.LocalEndPoint;

        /// <summary>
        /// 客户端连接状态
        /// </summary>
        public ConnectionState ConnectionState => _connectionState;

        /// <summary>
        /// Tcp 客户端选项
        /// </summary>
        public TcpClientOptions Options => _options;
        #endregion

        #region 事件
        public event Action Connected;
        public event Action Disconnected;
        public event Action<ConnectionState> ConnectionStateChanged;
        public event Action<Exception> ExceptionCaught;
        #endregion

        #region 方法
        #region 构造方法
        /// <summary>
        /// 使用给定的服务器 IP 地址和端口号初始化 TCP 客户端
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Tcp client options</param>
        public TcpClient(IPAddress address, int port, TcpClientOptions options = null) : this(new IPEndPoint(address, port), options) { }

        /// <summary>
        /// 使用给定的 host 和端口号初始化 TCP 客户端
        /// </summary>
        /// <param name="host">Host</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Tcp client options</param>
        public TcpClient(string host, int port, TcpClientOptions options = null) : this(new DnsEndPoint(host, port), options) { }

        /// <summary>
        /// 使用给定的 IP 端点初始化 TCP 客户端
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        /// <param name="options">Tcp client options</param>
        public TcpClient(EndPoint endpoint, TcpClientOptions options = null)
        {
            _id = Guid.NewGuid();
            _remoteEndpoint = endpoint;
            _options = options ?? new TcpClientOptions();
            ReadBuffer = new ReadBuffer(CollectRecvSaea, _options.ReceiveBufferSize);
        }
        #endregion

        #region 私有方法
        private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                // 确定刚刚完成的操作类型并调用关联的处理程序
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Connect:
                        ProcessConnect();
                        break;
                    case SocketAsyncOperation.Receive:
                        ProcessReceive(e);
                        break;
                    case SocketAsyncOperation.Send:
                        ProcessSend(e);
                        break;
                    default:
                        throw new ArgumentException("套接字上完成的最后一个操作不是连接、接收或发送");
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(e);
            }
        }

        private void ProcessConnect()
        {
            SocketAsyncEventArgs receiveSaea = null;
            try
            {
                // 如果在尝试连接中断开
                if (_connectEventArg == null)
                {
                    return;
                }

                if (_connectEventArg.SocketError != SocketError.Success)
                {
                    throw new SocketException((int)_connectEventArg.SocketError);
                }
                else
                {
                    ChangeConnectionState(ConnectionState.Connected);
                    OnConnected();

                    receiveSaea = _recvSaeaPool.Get();
                    if (_socketConnecter.ReceiveAsync(receiveSaea) == false)
                    {
                        ProcessReceive(receiveSaea);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(receiveSaea);
            }
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            try
            {
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
                    }
                    else
                    {
                        ReadBuffer.CacheSaea(e);

                        OnReceived();

                        // 如果在接收数据中断开, 直接关闭当前连接
                        if (_connectionState != ConnectionState.Connected)
                        {
                            ProcessClose(e);
                        }
                        else
                        {
                            SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Get();
                            if (_socketConnecter.ReceiveAsync(receiveSaea) == false)
                            {
                                ProcessReceive(receiveSaea);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(e);
            }
        }

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

                e.Dispose();
                
                if(_connectionState == ConnectionState.Connected || _connectionState == ConnectionState.Connecting)
                {
                    DisconnectAsync();
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
            if (exception is SocketException socketException)
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

        /// <summary>
        /// 改变连接状态
        /// </summary>
        /// <param name="connectionState"></param>
        private void ChangeConnectionState(ConnectionState connectionState)
        {
            _connectionState = connectionState;
            OnConnectionStateChanged(connectionState);
        }

        #region Send data to server
        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public virtual void SendAsync(byte[] buffer, int offset, int count)
        {
            try
            {
                if (buffer == null)
                {
                    throw new ArgumentNullException(nameof(buffer));
                }

                if (_connectionState != ConnectionState.Connected)
                {
                    throw new Exception("未成功连接服务端, 无法发送数据");
                }

                var e = _sendSaeaPool.Get();
                e.SetBuffer(buffer, offset, count);
                if (_socketConnecter.SendAsync(e) == false)
                {
                    ProcessSend(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="buffer"></param>
        public virtual void SendAsync(byte[] buffer)
        {
            SendAsync(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 向客户端发送数据（异步）
        /// </summary>
        /// <param name="bufferList"></param>
        public virtual void SendAsync(IList<ArraySegment<byte>> bufferList)
        {
            try
            {
                if (bufferList == null || bufferList.Count <= 0)
                {
                    throw new ArgumentNullException(nameof(bufferList));
                }

                if (_connectionState != ConnectionState.Connected)
                {
                    throw new Exception("未成功连接服务端, 无法发送数据");
                }

                var e = _sendSaeaPool.Get();
                e.BufferList = bufferList;
                if (_socketConnecter.SendAsync(e) == false)
                {
                    ProcessSend(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        #endregion

        #region Check connection timeout
        private System.Timers.Timer _connectionTimeoutChecker;
        private void CheckConnectionTimeout(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (_connectionState == ConnectionState.Connecting)
                {
                    throw new TimeoutException("连接超时, 或者连接的主机没有响应");
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                DisconnectAsync();
            }
        }
        #endregion

        private SocketAsyncEventArgs CreateRecvSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += OnAsyncCompleted;
            saea.SetBuffer(new byte[_options.ReceiveBufferSize], 0, _options.ReceiveBufferSize);
            return saea;
        }
        private SocketAsyncEventArgs CreateSendSaea()
        {
            var saea = new SocketAsyncEventArgs();
            saea.Completed += OnAsyncCompleted;
            return saea;
        }
        #endregion

        #region 保护方法
        /// <summary>
        /// 当发生非检查异常时调用
        /// </summary>
        /// <param name="exception"></param>
        protected virtual void OnExceptionCaught(Exception exception) 
        {
            ExceptionCaught?.Invoke(exception);
        }

        /// <summary>
        /// 创建一个新的套接字对象
        /// </summary>
        /// <remarks>
        /// 如果您需要在您的实现中准备一些特定的套接字对象, 则方法可能会被覆盖
        /// </remarks>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocketConnecter()
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

            // 应用选项：使用 keep-alive
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.KeepAlive);
            // 应用选项：不延迟直接发送
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, _options.NoDelay);

            return socket;
        }

        /// <summary>
        /// 连接成功后调用
        /// </summary>
        protected virtual void OnConnected() 
        {
            Connected?.Invoke();
        }

        /// <summary>
        /// 断开连接后调用
        /// </summary>
        protected virtual void OnDisconnected() 
        {
            Disconnected?.Invoke();
        }

        /// <summary>
        /// 收到数据后调用
        /// </summary>
        protected virtual void OnReceived() { }

        /// <summary>
        /// 连接状态改变后调用
        /// </summary>
        /// <param name="connectionState"></param>
        protected virtual void OnConnectionStateChanged(ConnectionState connectionState) 
        {
            ConnectionStateChanged?.Invoke(connectionState);
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 连接服务端（异步）
        /// </summary>
        public void ConnectAsync()
        {
            try
            {
                if (_connectionState != ConnectionState.Disconnected)
                {
                    throw new Exception("尝试连接失败, 已经连接成功或正在连接或正在断开");
                }

                ChangeConnectionState(ConnectionState.Connecting);

                _connectEventArg = new SocketAsyncEventArgs();
                _connectEventArg.Completed += OnAsyncCompleted;
                _connectEventArg.RemoteEndPoint = _remoteEndpoint;

                _socketConnecter = CreateSocketConnecter();

                _recvSaeaPool = new TcpReceiveSaeaPool(CreateRecvSaea);
                _sendSaeaPool = new SaeaPool(CreateSendSaea);

                _connectionTimeoutChecker = new System.Timers.Timer(_options.ConnectionTimeout);
                _connectionTimeoutChecker.Elapsed += CheckConnectionTimeout;
                _connectionTimeoutChecker.AutoReset = false;
                _connectionTimeoutChecker.Start();

                if (_socketConnecter.ConnectAsync(_connectEventArg) == false)
                {
                    ProcessConnect();
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        /// <summary>
        /// 重新连接（异步）
        /// </summary>
        public void ReconnectAsync()
        {
            if( _connectionState == ConnectionState.Connected)
            {
                DisconnectAsync();
            }

            ConnectAsync();
        }

        /// <summary>
        /// 断开连接（异步）
        /// </summary>
        public void DisconnectAsync()
        {
            try
            {
                // Cancel connecting operation
                if (_connectionState == ConnectionState.Connecting)
                {
                    Socket.CancelConnectAsync(_connectEventArg);
                }
                else if (_connectionState == ConnectionState.Connected)
                {
                    ChangeConnectionState(ConnectionState.Disconnecting);
                }
                else
                {
                    throw new Exception("尝试断开失败, 未连接成功或未正在连接");
                }

                _connectionTimeoutChecker.Stop();
                _connectionTimeoutChecker.Dispose();
                _connectionTimeoutChecker = null;

                ReadBuffer.Clear();
                _recvSaeaPool.Dispose();
                _recvSaeaPool = null;
                _sendSaeaPool.Dispose();
                _sendSaeaPool = null;
                _connectEventArg.Dispose();
                _connectEventArg = null;

                try
                {
                    try
                    {
                        _socketConnecter.Shutdown(SocketShutdown.Both);
                    }
                    catch (SocketException)
                    {
                    }

                    _socketConnecter.Dispose();
                    _socketConnecter = null;
                }
                catch (ObjectDisposedException) // 如果在尝试连接中断开, 即执行了 Socket.CancelConnectAsync
                {
                }

                ChangeConnectionState(ConnectionState.Disconnected);
                OnDisconnected();
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        #endregion

        #region 回收资源
        internal void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            try
            {
                if (_connectionState == ConnectionState.Connected)
                {
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
                if (_connectionState == ConnectionState.Connected)
                {
                    e.SetBuffer(null, 0, 0);
                    e.BufferList = null;
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
                DisconnectAsync();
            }
        }
        #endregion
        #endregion
    }
}
