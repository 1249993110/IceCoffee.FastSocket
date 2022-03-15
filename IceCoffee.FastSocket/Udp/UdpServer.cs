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

namespace IceCoffee.FastSocket.Udp
{
    /// <summary>
    /// UDP 服务器用于向 UDP 端点发送或多播数据报 
    /// </summary>
    /// <remarks>Thread-safe</remarks>
    public class UdpServer : IDisposable
    {
        #region 字段
        private readonly Guid _id;
        private volatile bool _isStarted;
        private IPEndPoint _endPoint;
        private IPEndPoint _multicastEndpoint;
        private readonly UdpServerOptions _options;

        private Socket _socket;
        private EndPoint _receiveEndpoint;
        private SaeaPool _recvSaeaPool;
        private SaeaPool _sendSaeaPool;
        #endregion

        #region 属性
        /// <summary>
        /// 服务端 Id
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// 是否已启动
        /// </summary>
        public bool IsStarted => _isStarted;

        /// <summary>
        /// IP 终结点
        /// </summary>
        public IPEndPoint EndPoint => _endPoint;

        /// <summary>
        /// 多播 IP 终结点
        /// </summary>
        public IPEndPoint MulticastEndpoint => _multicastEndpoint;

        /// <summary>
        /// Udp 服务端选项
        /// </summary>
        public UdpServerOptions Options => _options;
        #endregion

        #region 事件
        public event Action Started;
        public event Action Stopped;
        public event Action<Exception> ExceptionCaught;
        #endregion

        #region 方法
        #region 构造方法
        /// <summary>
        /// 使用给定的 IP 地址和端口号初始化 UDP 服务器
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Udp server options</param>
        public UdpServer(IPAddress address, int port, UdpServerOptions options = null)
            : this(new IPEndPoint(address, port), options)
        {
        }

        /// <summary>
        /// 使用给定的 IP 地址和端口号初始化 UDP 服务器
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options"></param>
        public UdpServer(string address, int port, UdpServerOptions options = null)
            : this(new IPEndPoint(IPAddress.Parse(address), port), options)
        {
        }

        /// <summary>
        /// 使用给定的 IP 端点初始化 UDP 服务器
        /// </summary>
        /// <param name="endPoint">IP end point</param>
        /// <param name="options"></param>
        public UdpServer(IPEndPoint endPoint, UdpServerOptions options = null)
        {
            _id = Guid.NewGuid();
            _endPoint = endPoint;
            _options = options ?? new UdpServerOptions();
            
        }
        #endregion

        #region 私有方法
        #region Recv data from clients
        /// <summary>
        /// Start receive data
        /// </summary>
        private void StartReceive()
        {
            try
            {
                var e = _recvSaeaPool.Get();

                if (_socket.ReceiveFromAsync(e) == false)
                {
                    ProcessReceiveFrom(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        private void ProcessReceiveFrom(SocketAsyncEventArgs e)
        {
            try
            {
                if(_isStarted == false)
                {
                    return;
                }

                Task.Run(StartReceive);

                SocketError socketError = e.SocketError;
                if (socketError != SocketError.Success)
                {
                    throw new SocketException((int)socketError);
                }
                else
                {
                    OnReceived(e.RemoteEndPoint, e.Buffer, e.BytesTransferred);
                    CollectRecvSaea(e);
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
            ProcessReceiveFrom(e);
        }
        #endregion

        #region Send data to clients
        private void ProcessSendTo(SocketAsyncEventArgs e)
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
            ProcessSendTo(e);
        }
        /// <summary>
        /// 将数据报发送到给定的端点（异步） 
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public virtual void SendAsync(EndPoint endPoint, byte[] buffer, int offset, int count)
        {
            try
            {
                if (endPoint == null)
                {
                    throw new ArgumentNullException(nameof(endPoint));
                }

                if (buffer == null)
                {
                    throw new ArgumentNullException(nameof(buffer));
                }

                if (_isStarted == false)
                {
                    throw new Exception("服务端尚未启动，无法发送数据");
                }

                var e = _sendSaeaPool.Get();
                e.RemoteEndPoint = endPoint;
                e.SetBuffer(buffer, offset, count);

                if (_socket.SendToAsync(e) == false)
                {
                    ProcessSendTo(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }
        /// <summary>
        /// 将数据报发送到给定的端点（异步） 
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="buffer"></param>
        public virtual void SendAsync(EndPoint endPoint, byte[] buffer)
        {
            SendAsync(endPoint, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 将数据报发送到给定的端点（异步） 
        /// </summary>
        /// <param name="endPoint"></param>
        /// <param name="bufferList"></param>
        public virtual void SendAsync(EndPoint endPoint, IList<ArraySegment<byte>> bufferList)
        {
            try
            {
                if (endPoint == null)
                {
                    throw new ArgumentNullException(nameof(endPoint));
                }

                if (bufferList == null || bufferList.Count <= 0)
                {
                    throw new ArgumentNullException(nameof(bufferList));
                }

                if (_isStarted == false)
                {
                    throw new Exception("服务端尚未启动，无法发送数据");
                }

                var e = _sendSaeaPool.Get();
                e.RemoteEndPoint = endPoint;
                e.BufferList = bufferList;

                if (_socket.SendToAsync(e) == false)
                {
                    ProcessSendTo(e);
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
            }
        }

        /// <summary>
        /// 多播数据报到准备好的多播端点的（异步） 
        /// </summary>
        /// <param name="buffer">Datagram buffer to multicast</param>
        /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
        public virtual void MulticastAsync(byte[] buffer) 
        {
            SendAsync(_multicastEndpoint, buffer, 0, buffer.Length); 
        }

        /// <summary>
        /// 多播数据报到准备好的多播端点的（异步） 
        /// </summary>
        /// <param name="buffer">Datagram buffer to multicast</param>
        /// <param name="offset">Datagram buffer offset</param>
        /// <param name="size">Datagram buffer size</param>
        /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
        public virtual void MulticastAsync(byte[] buffer, int offset, int size) 
        { 
            SendAsync(_multicastEndpoint, buffer, offset, size); 
        }

        /// <summary>
        /// 多播数据报到准备好的多播端点的（异步） 
        /// </summary>
        /// <param name="bufferList"></param>
        /// <returns>'true' if the datagram was successfully multicasted, 'false' if the datagram was not multicasted</returns>
        public virtual void MulticastAsync(IList<ArraySegment<byte>> bufferList)
        {
            SendAsync(_multicastEndpoint, bufferList);
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

                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.ReceiveFrom:
                        CollectRecvSaea(e);
                        break;
                    case SocketAsyncOperation.SendTo:
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
                // 跳过断开连接错误 
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
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += OnRecvAsyncCompleted;
            saea.RemoteEndPoint = _receiveEndpoint;
            saea.SetBuffer(new byte[_options.ReceiveBufferSize], 0, _options.ReceiveBufferSize);
            return saea;
        }
        private SocketAsyncEventArgs CreateSendSaea()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += OnSendAsyncCompleted;
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
        /// 如果您需要在您的实现中准备一些特定的套接字对象，则方法可能会被覆盖
        /// </remarks>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocket()
        {
            var socket = new Socket(_endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            
            // 应用选项：重用地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, _options.ReuseAddress);
            // 应用选项：独占地址
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, _options.ExclusiveAddressUse);

            // 应用选项：双模式（必须在接收前应用此选项）
            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                socket.DualMode = _options.DualMode;
            }

            return socket;
        }

        /// <summary>
        /// 当服务端开始后调用
        /// </summary>
        protected virtual void OnStarted() 
        {
            Started?.Invoke();
        }

        /// <summary>
        /// 当服务端停止后调用
        /// </summary>
        protected virtual void OnStopped() 
        {
            Stopped?.Invoke();
        }

        /// <summary>
        /// 当收到数据报后调用
        /// </summary>
        /// <param name="endpoint">Received endpoint</param>
        /// <param name="buffer">Received datagram buffer</param>
        /// <param name="size">Received datagram buffer size</param>
        /// <remarks>
        /// Notification is called when another datagram was received from some endpoint
        /// </remarks>
        protected virtual void OnReceived(EndPoint endpoint, byte[] buffer, int size) { }
        #endregion

        #region 公开方法
        /// <summary>
        /// 启动服务器
        /// </summary>
        public virtual void Start()
        {
            try
            {
                if (_isStarted)
                {
                    throw new Exception("服务端已经开始监听");
                }

                // 创建一个新的套接字接受器
                _socket = CreateSocket();
                // 将接受者套接字绑定到 IP 终结点
                _socket.Bind(_endPoint);
                // 根据创建的实际端点刷新端点属性
                _endPoint = (IPEndPoint)_socket.LocalEndPoint;

                _receiveEndpoint = new IPEndPoint((_endPoint.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any, 0);

                _recvSaeaPool = new SaeaPool(CreateRecvSaea);
                _sendSaeaPool = new SaeaPool(CreateSendSaea);

                _isStarted = true;
                OnStarted();

                if (_multicastEndpoint != null)
                {
                    // 开始接受数据
                    StartReceive();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UdpServer.Start", ex);
            }
        }

        /// <summary>
        /// 使用给定的多播 IP 地址和端口号启动服务器（同步）
        /// </summary>
        /// <param name="multicastAddress">Multicast IP address</param>
        /// <param name="multicastPort">Multicast port number</param>
        /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
        public virtual void Start(IPAddress multicastAddress, int multicastPort) 
        { 
            Start(new IPEndPoint(multicastAddress, multicastPort)); 
        }

        /// <summary>
        /// 使用给定的多播 IP 地址和端口号启动服务器（同步）
        /// </summary>
        /// <param name="multicastAddress">Multicast IP address</param>
        /// <param name="multicastPort">Multicast port number</param>
        /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
        public virtual void Start(string multicastAddress, int multicastPort) 
        { 
            Start(new IPEndPoint(IPAddress.Parse(multicastAddress), multicastPort)); 
        }

        /// <summary>
        /// 使用给定的多播端点启动服务器（同步）
        /// </summary>
        /// <param name="multicastEndpoint">Multicast IP endpoint</param>
        /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
        public virtual void Start(IPEndPoint multicastEndpoint)
        {
            _multicastEndpoint = multicastEndpoint;
            Start();
        }

        /// <summary>
        /// 重新启动服务器
        /// </summary>
        /// <returns></returns>
        public virtual void Restart()
        {
            if (_isStarted)
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
                if (_isStarted == false)
                {
                    throw new Exception("服务端已经停止监听");
                }

                _isStarted = false;

                _recvSaeaPool.Dispose();
                _recvSaeaPool = null;
                _sendSaeaPool.Dispose();
                _sendSaeaPool = null;
                _socket.Dispose();
                _socket = null;

                OnStopped();
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UdpServer.Stop", ex);
            }
        }
        #endregion

        #region 回收资源
        private void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            try
            {
                if (_isStarted)
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
                if (_isStarted)
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
