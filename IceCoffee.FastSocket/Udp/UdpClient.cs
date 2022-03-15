using IceCoffee.FastSocket.Pools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Udp
{
    public class UdpClient : IDisposable
    {
        #region 字段
        private readonly Guid _id;
        private volatile bool _isOpened;
        private EndPoint _remoteEndpoint;
        private EndPoint _receiveEndpoint;
        private readonly UdpClientOptions _options;

        private SaeaPool _recvSaeaPool;
        private SaeaPool _sendSaeaPool;
        private Socket _socket;
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
        public EndPoint LocalEndPoint => _socket?.LocalEndPoint;

        /// <summary>
        /// 客户端是否已经开启
        /// </summary>
        public bool IsOpened => _isOpened;

        /// <summary>
        /// Udp 客户端选项
        /// </summary>
        public UdpClientOptions Options => _options;
        #endregion

        #region 事件
        public event Action Opened;
        public event Action Closed;
        public event Action<IPAddress> JoinedMulticastGroup;
        public event Action<IPAddress> LeftMulticastGroup;
        public event Action<Exception> ExceptionCaught;
        #endregion

        #region 方法
        #region 构造方法
        /// <summary>
        /// 使用给定的服务器 IP 地址和端口号初始化 UDP 客户端
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Udp client options</param>
        public UdpClient(IPAddress address, int port, UdpClientOptions options = null) : this(new IPEndPoint(address, port), options) { }

        /// <summary>
        /// 使用给定的 host 和端口号初始化 UDP 客户端
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        /// <param name="options">Udp client options</param>
        public UdpClient(string address, int port, UdpClientOptions options = null) : this(new IPEndPoint(IPAddress.Parse(address), port), options) { }

        /// <summary>
        /// 使用给定的 IP 端点初始化 UDP 客户端
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        /// <param name="options">Udp client options</param>
        public UdpClient(EndPoint endpoint, UdpClientOptions options = null)
        {
            _id = Guid.NewGuid();
            _remoteEndpoint = endpoint;
            _options = options ?? new UdpClientOptions();
        }
        #endregion

        #region 私有方法
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
                if (_isOpened == false)
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
        private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                // 确定刚刚完成的操作类型并调用关联的处理程序
                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.ReceiveFrom:
                        ProcessReceiveFrom(e);
                        break;
                    case SocketAsyncOperation.SendTo:
                        ProcessSendTo(e);
                        break;
                    default:
                        throw new ArgumentException("套接字上完成的最后一个操作不是接收或发送");
                }
            }
            catch (Exception ex)
            {
                RaiseException(ex);
                ProcessClose(e);
            }
        }

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

                if (_isOpened)
                {
                    Close();
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

        #region Send data to server
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

                if (_isOpened == false)
                {
                    throw new Exception("套接字尚未打开，无法发送数据");
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
                if (bufferList == null || bufferList.Count <= 0)
                {
                    throw new ArgumentNullException(nameof(bufferList));
                }

                if (_isOpened == false)
                {
                    throw new Exception("套接字尚未打开，无法发送数据");
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
        /// 将数据报发送到预设的端点（异步） 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public virtual void SendAsync(byte[] buffer, int offset, int count)
        {
            SendAsync(_remoteEndpoint, buffer, offset, count);
        }

        /// <summary>
        /// 将数据报发送到预设的端点（异步） 
        /// </summary>
        /// <param name="buffer"></param>
        public virtual void SendAsync(byte[] buffer)
        {
            SendAsync(_remoteEndpoint, buffer, 0, buffer.Length);
        }

        /// <summary>
        /// 将数据报发送到预设的端点（异步）
        /// </summary>
        /// <param name="bufferList"></param>
        public virtual void SendAsync(IList<ArraySegment<byte>> bufferList)
        {
            SendAsync(_remoteEndpoint, bufferList);
        }
        #endregion

        private SocketAsyncEventArgs CreateRecvSaea()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += OnAsyncCompleted;
            saea.RemoteEndPoint = _receiveEndpoint;
            saea.SetBuffer(new byte[_options.ReceiveBufferSize], 0, _options.ReceiveBufferSize);
            return saea;
        }
        private SocketAsyncEventArgs CreateSendSaea()
        {
            SocketAsyncEventArgs saea = new SocketAsyncEventArgs();
            saea.Completed += OnAsyncCompleted;
            return saea;
        }
        #endregion

        #region 保护方法
        /// <summary>
        /// 设置多播：将套接字绑定到多播 UDP 服务器 
        /// </summary>
        /// <param name="enable">Enable/disable multicast</param>
        public virtual void SetupMulticast(bool enable)
        {
            _options.ReuseAddress = enable;
            _options.Multicast = enable;
        }

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
            var socket = new Socket(_remoteEndpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

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
        /// 客户端打开后调用
        /// </summary>
        protected virtual void OnOpened() 
        {
            Opened?.Invoke();
        }

        /// <summary>
        /// 客户端关闭后调用
        /// </summary>
        protected virtual void OnClosed() 
        {
            Closed?.Invoke();
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

        /// <summary>
        /// 加入具有给定 IP 地址的多播组后调用
        /// </summary>
        /// <param name="ipAddress"></param>
        protected virtual void OnJoinedMulticastGroup(IPAddress ipAddress) 
        {
            JoinedMulticastGroup?.Invoke(ipAddress);
        }

        /// <summary>
        /// 离开具有给定 IP 地址的多播组后调用
        /// </summary>
        /// <param name="ipAddress"></param>
        protected virtual void OnLeftMulticastGroup(IPAddress ipAddress) 
        {
            LeftMulticastGroup?.Invoke(ipAddress);
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 开启客户端
        /// </summary>
        public void Open()
        {
            try
            {
                if (_isOpened)
                {
                    throw new Exception("尝试打开失败，客户端已经打开");
                }

                _socket = CreateSocket();

                _receiveEndpoint = new IPEndPoint((_remoteEndpoint.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any, 0);

                if (_options.Multicast)
                {
                    _socket.Bind(_remoteEndpoint);
                }
                else
                {
                    _socket.Bind(_receiveEndpoint);
                }

                _recvSaeaPool = new SaeaPool(CreateRecvSaea);
                _sendSaeaPool = new SaeaPool(CreateSendSaea);

                _isOpened = true;
                OnOpened();

                StartReceive();
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UpdClient.Open", ex);
            }
        }

        /// <summary>
        /// 关闭客户端
        /// </summary>
        public void Close()
        {
            try
            {
                if (_isOpened == false)
                {
                    throw new Exception("尝试关闭失败，客户端尚未打开");
                }

                _recvSaeaPool.Dispose();
                _recvSaeaPool = null;
                _sendSaeaPool.Dispose();
                _sendSaeaPool = null;

                _socket.Dispose();
                _socket = null;

                _isOpened = false;
                OnClosed();
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UpdClient.Close", ex);
            }
        }

        #region Multicast group
        /// <summary>
        /// 加入具有给定 IP 地址的多播组（同步） 
        /// </summary>
        /// <param name="address">IP address</param>
        public virtual void JoinMulticastGroup(IPAddress address)
        {
            try
            {
                if (_remoteEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(address));
                }
                else
                {
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(address));
                }

                OnJoinedMulticastGroup(address);
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UpdClient.JoinMulticastGroup", ex);
            }
        }
        /// <summary>
        /// 加入具有给定 IP 地址的多播组（同步） 
        /// </summary>
        /// <param name="address">IP address</param>
        public virtual void JoinMulticastGroup(string address) 
        { 
            JoinMulticastGroup(IPAddress.Parse(address)); 
        }

        /// <summary>
        /// 使用给定的 IP 地址离开多播组（同步） 
        /// </summary>
        /// <param name="address">IP address</param>
        public virtual void LeaveMulticastGroup(IPAddress address)
        {
            try
            {
                if (_remoteEndpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.DropMembership, new IPv6MulticastOption(address));
                }
                else
                {
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership, new MulticastOption(address));
                }

                OnLeftMulticastGroup(address);
            }
            catch (Exception ex)
            {
                throw new Exception("Error in UpdClient.LeaveMulticastGroup", ex);
            }
        }
        /// <summary>
        /// 使用给定的 IP 地址离开多播组（同步） 
        /// </summary>
        /// <param name="address">IP address</param>
        public virtual void LeaveMulticastGroup(string address) 
        { 
            LeaveMulticastGroup(IPAddress.Parse(address)); 
        }

        #endregion
        #endregion

        #region 回收资源
        private void CollectRecvSaea(SocketAsyncEventArgs e)
        {
            try
            {
                if (_isOpened)
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
                if (_isOpened)
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
                Close();
            }
        }
        #endregion
        #endregion
    }
}
