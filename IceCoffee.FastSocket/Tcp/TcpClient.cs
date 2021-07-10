using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    public class TcpClient : IDisposable
    {
        #region 字段
        private readonly Guid _id;
        private bool _isConnecting;
        private bool _isConnected;
        private IPEndPoint _remoteEndpoint;
        private readonly TcpClientOptions _options;

        private Socket _socketConnecter;
        private SocketAsyncEventArgs _connectEventArg;
        private SocketAsyncEventArgs _receiveEventArg;
        private SocketAsyncEventArgs _sendEventArg;
        #endregion

        #region 属性
        /// <summary>
        /// 客户端 Id
        /// </summary>
        public Guid Id => _id;

        /// <summary>
        /// IP 终结点
        /// </summary>
        public IPEndPoint RemoteEndpoint => _remoteEndpoint;

        /// <summary>
        /// 客户端是否正在连接
        /// </summary>
        public bool IsConnecting => _isConnecting;

        /// <summary>
        /// 客户端是否已经连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// Tcp 客户端选项
        /// </summary>
        public TcpClientOptions Options => _options;
        #endregion

        #region 方法
        #region 构造方法
        public TcpClient()
        {
            _id = Guid.NewGuid();
        }

        #endregion

        #region 私有方法
        private void OnAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (_isConnected == false && _isConnecting == false)
            {
                return;
            }

            // 确定刚刚完成的操作类型并调用关联的处理程序
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Connect:
                    ProcessConnect(e);
                    break;
                case SocketAsyncOperation.Receive:
                    if (ProcessReceive(e))
                        TryReceive();
                    break;
                case SocketAsyncOperation.Send:
                    if (ProcessSend(e))
                        TrySend();
                    break;
                default:
                    throw new ArgumentException("套接字上完成的最后一个操作不是接收或发送");
            }

        }


        private void OnConnectAsyncCompleted(object sender, SocketAsyncEventArgs e)
        {
            SocketError socketError = e.SocketError;
            if (socketError != SocketError.Success)
            {
                throw new NetworkException("Socket已关闭，重叠的操作被中止，SocketError：" + socketError.ToString());
            }

            int sessionID = _socketConnecter.Handle.ToInt32();
            SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Take();

            _session.Attach(_socketConnecter, sessionID);

            try
            {
                OnInternalConnectionStateChanged(ConnectionState.Connected);
                OnConnected();
                if (_socketConnecter.ReceiveAsync(receiveSaea) == false)
                {
                    Task.Run(() =>
                    {
                        OnRecvAsyncRequestCompleted(_socketConnecter, receiveSaea);
                    });
                }
            }
            catch
            {
                ProcessClose(CloseReason.ApplicationError);
                throw;
            }
        }

        private void OnRecvAsyncRequestCompleted(object sender, SocketAsyncEventArgs e)
        {
            SocketError socketError = e.SocketError;
            if (e.BytesTransferred > 0 && socketError == SocketError.Success)
            {
                try
                {
                    _session.ReadBuffer.CacheSaea(e);

                    // 主动关闭会话
                    if (_session.socket == null)
                    {
                        ProcessClose(CloseReason.ClientClosing);
                    }
                    else
                    {
                        SocketAsyncEventArgs receiveSaea = _recvSaeaPool.Take();

                        if (_socketConnecter.ReceiveAsync(receiveSaea) == false)
                        {
                            OnRecvAsyncRequestCompleted(sender, receiveSaea);
                        }
                    }
                }
                catch
                {
                    ProcessClose(CloseReason.InternalError);
                    throw;
                }
            }
            else
            {
                ProcessClose(CloseReason.SocketError);
                throw new NetworkException("异常socket连接，SocketError：" + socketError);
            }
        }


        private void ProcessClose(CloseReason closeReason)
        {
            if (_connectionState == ConnectionState.Connected) //已连接
            {
                _session.Close();

                OnInternalConnectionStateChanged(ConnectionState.Disconnected);
                _session.OnClosed(closeReason);
                OnDisconnected(closeReason);
                _session.Detach();

                _session.ReadBuffer.CollectAllRecvSaeaAndReset();

                _session = null;

                if (_autoReconnectMaxCount > 0 && _autoReconnectCount == 0)
                {
                    AutoReconnect();
                }
            }
        }

        private void OnInternalSend(ITcpSessionBase session, byte[] data, int offset, int count)
        {
            if (count <= _sendBufferSize)// 如果count小于发送缓冲区大小，此时count应不大于data.Length
            {
                SocketAsyncEventArgs sendSaea = _sendSaeaPool.Take();
                Array.Copy(data, offset, sendSaea.Buffer, 0, count);
                sendSaea.SetBuffer(0, count);
                if (_socketConnecter.SendAsync(sendSaea) == false)
                {
                    OnSendAsyncRequestCompleted(_socketConnecter, sendSaea);
                }
            }
        }

        private void OnSendAsyncRequestCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                ProcessClose(CloseReason.SocketError);
                throw new NetworkException("异常socket连接，SocketError：" + e.SocketError);
            }

            _sendSaeaPool.Put(e);
        }
        #endregion

        #region 保护方法
        // <summary>
        /// 创建一个新的套接字接受器对象
        /// </summary>
        /// <remarks>
        /// 如果您需要在您的实现中准备一些特定的套接字对象，则方法可能会被覆盖
        /// </remarks>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocketConnecter()
        {
            var socket = new Socket(_remoteEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            // 应用选项：使用 keep-alive
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _options.KeepAlive);
            // 应用选项：不延迟直接发送
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, _options.NoDelay);

            return socket;
        }

        /// <summary>
        /// 连接成功时调用
        /// </summary>
        protected virtual void OnConnected()
        {
            
        }

        /// <summary>
        /// 断开连接时调用
        /// </summary>
        protected virtual void OnDisconnected()
        {
        }

        #endregion

        #region 公开方法
        public void Connect(string ipStr, ushort port)
        {
            if (_socketConnecter != null && _socketConnecter.Connected)
            {
                DisconnectAsync();
            }

            _isConnecting = true;

            _connectEventArg = new SocketAsyncEventArgs();
            _connectEventArg.Completed += OnConnectAsyncCompleted;

            _socketConnecter = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _session = new TSession();
            _session.Initialize(this, OnInternalSend, _recvSaeaPool.Put);

            bool isNumberIP = IPAddress.TryParse(ipStr, out _ipAddress);// 数字IP
            if (isNumberIP == false)// 域名
            {
                _ipAddress = Dns.GetHostEntry(ipStr).AddressList.Where(p => p.AddressFamily == AddressFamily.InterNetwork).First();
            }

            _connectEventArg.RemoteEndPoint = new IPEndPoint(_ipAddress, _port);

            if (_socketConnecter.ConnectAsync(_connectEventArg) == false)
            {
                OnConnectAsyncCompleted(_socketConnecter, _connectEventArg);
            }
        }


        public void Reconnect()
        {
            Connect(_ipAddress.ToString(), _port);
        }

        public bool DisconnectAsync()
        {
            if (_isConnected == false && _isConnecting == false)
            {
                return false;
            }    

            // Cancel connecting operation
            if (_isConnecting)
            {
                Socket.CancelConnectAsync(_connectEventArg);
            }

            // Reset event args
            _connectEventArg.Completed -= OnAsyncCompleted;
            _receiveEventArg.Completed -= OnAsyncCompleted;
            _sendEventArg.Completed -= OnAsyncCompleted;


            if (_session != null && _connectionState != ConnectionState.Disconnected)
            {
                OnInternalConnectionStateChanged(ConnectionState.Disconnecting);

                _session.Close();
                _recvSaeaPool.Dispose();
                _sendSaeaPool.Dispose();

                OnInternalConnectionStateChanged(ConnectionState.Disconnected);
                _session.OnClosed(CloseReason.ClientClosing);
                OnDisconnected(CloseReason.ClientClosing);
                _session.Detach();

                _session = null;
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
