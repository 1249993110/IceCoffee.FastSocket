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

        private ReceiveSaeaPool _recvSaeaPool;
        private SendSaeaPool _sendSaeaPool;
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
        /// <param name="host">IP address</param>
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

                    receiveSaea = _recvSaeaPool.Take();
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

                        // 如果在接收数据中断开，直接关闭当前连接
                        if (_connectionState != ConnectionState.Connected)
                        {
                            ProcessClose(e);
                        }
                        else
                        {
                            SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Take();
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

            OnException(exception);
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
            if (_connectionState != ConnectionState.Connected || count <= 0)
            {
                return;
            }

            var e = _sendSaeaPool.Take();
            e.SetBuffer(buffer, offset, count);
            if (_socketConnecter.SendAsync(e) == false)
            {
                ProcessSend(e);
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
            if (_connectionState != ConnectionState.Connected || bufferList.Count <= 0)
            {
                return;
            }

            var e = _sendSaeaPool.Take();
            e.BufferList = bufferList;
            if (_socketConnecter.SendAsync(e) == false)
            {
                ProcessSend(e);
            }
        }
        #endregion

        #region Check connection timeout
        private CancellationTokenSource _checkConnectionTimeoutCTS;
        private void CheckConnectionTimeout(Task task)
        {
            try
            {
                if (_checkConnectionTimeoutCTS == null || _checkConnectionTimeoutCTS.IsCancellationRequested)
                {
                    return;
                }

                if (_connectionState == ConnectionState.Connecting)
                {
                    throw new TimeoutException("连接尝试超时，或者连接的主机没有响应");
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                DisconnectAsync();
            }
        }
        #endregion
        #endregion

        #region 保护方法
        /// <summary>
        /// 当发生非检查异常时调用
        /// </summary>
        /// <param name="exception"></param>
        protected virtual void OnException(Exception exception) { }

        /// <summary>
        /// 创建一个新的套接字接受器对象
        /// </summary>
        /// <remarks>
        /// 如果您需要在您的实现中准备一些特定的套接字对象，则方法可能会被覆盖
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
        /// 连接成功时调用
        /// </summary>
        protected virtual void OnConnected() {  }

        /// <summary>
        /// 断开连接时调用
        /// </summary>
        protected virtual void OnDisconnected() { }

        /// <summary>
        /// 收到数据后调用
        /// </summary>
        protected virtual void OnReceived() { }

        /// <summary>
        /// 连接状态改变时调用
        /// </summary>
        /// <param name="connectionState"></param>
        protected virtual void OnConnectionStateChanged(ConnectionState connectionState) { }
        #endregion

        #region 公开方法
        /// <summary>
        /// 连接服务端（异步）
        /// </summary>
        public void ConnectAsync()
        {
            lock (this)
            {
                if (_connectionState != ConnectionState.Disconnected)
                {
                    throw new Exception("尝试连接失败，已经连接成功或正在连接或正在断开");
                }

                ChangeConnectionState(ConnectionState.Connecting);

                _recvSaeaPool = new ReceiveSaeaPool(OnAsyncCompleted, _options.ReceiveBufferSize);
                _sendSaeaPool = new SendSaeaPool(OnAsyncCompleted);

                _connectEventArg = new SocketAsyncEventArgs();
                _connectEventArg.Completed += OnAsyncCompleted;
                _connectEventArg.RemoteEndPoint = _remoteEndpoint;

                _socketConnecter = CreateSocketConnecter();

                _checkConnectionTimeoutCTS = new CancellationTokenSource();
                Task.Delay(_options.ConnectionTimeout, _checkConnectionTimeoutCTS.Token).ContinueWith(CheckConnectionTimeout, _checkConnectionTimeoutCTS.Token);

                if (_socketConnecter.ConnectAsync(_connectEventArg) == false)
                {
                    ProcessConnect();
                }
            }
        }

        /// <summary>
        /// 重新连接（异步）
        /// </summary>
        public void ReconnectAsync()
        {
            DisconnectAsync();
            ConnectAsync();
        }

        /// <summary>
        /// 断开连接（异步）
        /// </summary>
        public void DisconnectAsync()
        {
            lock (this)
            {
                if (_connectionState != ConnectionState.Connected && _connectionState != ConnectionState.Connecting)
                {
                    throw new Exception("尝试断开失败，未连接成功且未正在连接");
                }

                // Cancel connecting operation
                if (_connectionState == ConnectionState.Connecting)
                {
                    Socket.CancelConnectAsync(_connectEventArg);
                    _checkConnectionTimeoutCTS.Cancel(false);
                }

                ChangeConnectionState(ConnectionState.Disconnecting);

                _checkConnectionTimeoutCTS.Dispose();
                _checkConnectionTimeoutCTS = null;

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
                catch (ObjectDisposedException) // 如果在尝试连接中断开，即执行了 Socket.CancelConnectAsync
                { 
                }

                ChangeConnectionState(ConnectionState.Disconnected);
                OnDisconnected();
            }
        }

        #endregion

        #region 回收资源
        internal void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            if (_connectionState == ConnectionState.Connected)
            {
                _recvSaeaPool.Put(e);
            }
            else
            {
                e.Dispose();
            }
        }
        private void CollectSendSaea(SocketAsyncEventArgs e)
        {
            if (_connectionState == ConnectionState.Connected)
            {
                e.SetBuffer(null, 0, 0);
                e.BufferList = null;
                _sendSaeaPool.Put(e);
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
                    DisconnectAsync();
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _isDisposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~TcpClient()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion
        #endregion
    }
}
