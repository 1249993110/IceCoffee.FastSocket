using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace IceCoffee.FastSocket.Tcp
{
    /// <summary>
    /// 读取缓冲区
    /// </summary>
    public class ReadBuffer
    {
        #region 不固定字段，回收时重置
        /// <summary>
        /// 待读取的 saea 队列
        /// </summary>
        private Queue<SocketAsyncEventArgs> _queue;

        /// <summary>
        /// 当前缓冲区中待读取的字节数
        /// </summary>
        private long _bytesAvailable = 0;

        /// <summary>
        /// 当前buffer列表第一个元素（上次被读取到Saea中Buffer数据）的偏移的偏移量）
        /// </summary>
        private int _readOffset = 0;

        /// <summary>
        /// 上次搜索到'\n'符的位置
        /// </summary>
        private long _newlineIndex = -1;
        #endregion

        #region 固定字段
        /// <summary>
        /// 选项：接收缓冲区大小
        /// </summary>
        private readonly int _receiveBufferSize;

        /// <summary>
        /// 最大 saea 缓存个数
        /// </summary>
        private int _readBufferMaxCount = 128;

        /// <summary>
        /// 内部回收 saea 委托
        /// </summary>
        private readonly Action<SocketAsyncEventArgs> _collectSaea;
        #endregion 固定字段

        #region 属性
        /// <summary>
        /// <para>能否从读取缓冲区读取一行数据，通常在 ReadLine 前调用</para>
        /// <para>如果换行符 ASCII（'\n'）包含在缓冲区中返回 true，否则返回 false</para>
        /// </summary>
        public bool CanReadLine
        {
            get
            {
                if (_newlineIndex == -1)
                {
                    _newlineIndex = IndexOf(10);
                }

                return _newlineIndex != -1;
            }
        }

        /// <summary>
        /// <para>设置或者内部读取缓冲区的最大长度（字节），读取缓冲区溢出将导致会话关闭,</para>
        /// <para>缓冲区大小值小于或0意味着读取缓冲区不受限制，并且所有传入数据都被缓冲。默认值为 1M</para>
        /// <para>如果您只在某些时间点（例如，在实时流应用程序中）读取数据</para>
        /// <para>或者如果您希望保护套接字不受太多数据的影响（这些数据最终可能会导致应用程序内存不足），则此选项非常有用</para>
        /// <para>内部读取缓冲区的大小必须为 ReceiveBufferSize（每次接收数据的缓冲区大小）的整数倍</para>
        /// </summary>
        public long ReadBufferMaxLength
        {
            get => (long)_readBufferMaxCount * _receiveBufferSize;
            set => _readBufferMaxCount = (int)(value / _receiveBufferSize);
        }

        /// <summary>
        /// 返回缓冲区中等待读取的字节总数
        /// </summary>
        public long BytesAvailable => _bytesAvailable;
        #endregion 属性

        #region 公开方法
        /// <summary>
        /// 从缓冲区读取最多maxSize字节，并返回以字节数组形式读取的数据。
        /// <para>缓冲区为空、maxSize小于或等于0将返回空的字节数组</para>
        /// </summary>
        /// <param name="maxSize"></param>
        /// <returns></returns>
        public byte[] Read(long maxSize = 0)
        {
            _newlineIndex = -1;
            if (_queue.Count == 0 || maxSize <= 0)// 缓冲区为空
            {
                return new byte[0];
            }
            else if (_bytesAvailable <= maxSize)// 移除所有
            {
                return ReadAll();
            }
            else// 移除部分,此时队列中应至少有1个saea
            {
                byte[] result = new byte[maxSize];
                long alreadyRemoveCount = 0;// 已移除大小 

                SocketAsyncEventArgs saea = _queue.Peek();
                long willRemoveCount = saea.BytesTransferred - _readOffset;// 将移除大小，第一个buffer的大小

                if (willRemoveCount > maxSize)// 截取第一个saea的部分即可
                {
                    Array.Copy(saea.Buffer, _readOffset, result, 0, maxSize);
                    _readOffset += (int)maxSize;
                    _bytesAvailable -= maxSize;
                    return result;
                }
                _queue.Dequeue();
                _bytesAvailable -= willRemoveCount;
                Array.Copy(saea.Buffer, _readOffset, result, 0, willRemoveCount);
                alreadyRemoveCount += willRemoveCount;
                _readOffset = 0;
                _collectSaea.Invoke(saea);
                if (alreadyRemoveCount == maxSize)// 取第一个saea全部即可
                {
                    return result;
                }
                else//需要在后面的saea中继续取
                {
                    do
                    {
                        saea = _queue.Peek();
                        willRemoveCount = alreadyRemoveCount + saea.BytesTransferred;
                        if (willRemoveCount < maxSize)//不够
                        {
                            _queue.Dequeue();
                            _bytesAvailable -= saea.BytesTransferred;
                            Array.Copy(saea.Buffer, 0, result, alreadyRemoveCount, saea.BytesTransferred);
                            alreadyRemoveCount += saea.BytesTransferred;
                            _collectSaea.Invoke(saea);
                            continue;
                        }
                        else if (willRemoveCount == maxSize)//刚好
                        {
                            _queue.Dequeue();
                            _bytesAvailable -= saea.BytesTransferred;
                            Array.Copy(saea.Buffer, 0, result, alreadyRemoveCount, saea.BytesTransferred);
                            _collectSaea.Invoke(saea);
                            break;
                        }
                        else//大于，截取最后一个saea的部分
                        {
                            _readOffset = (int)(maxSize - alreadyRemoveCount);
                            _bytesAvailable -= _readOffset;
                            Array.Copy(saea.Buffer, 0, result, alreadyRemoveCount, _readOffset);
                            break;
                        }
                    } while (_queue.Count > 0);
                }

                return result;
            }
        }

        /// <summary>
        /// <para>从缓冲区读取所有剩余数据，并将其作为字节数组返回。</para>
        /// <para>此方法无法报告错误；返回空的字节数组可能意味着当前没有可供读取的数据，或者发生错误。</para>
        /// </summary>
        /// <returns></returns>
        public byte[] ReadAll()
        {
            _newlineIndex = -1;
            if (_queue.Count == 0)
            {
                return new byte[0];
            }

            byte[] result = new byte[_bytesAvailable];
            long alreadyRemoveCount = 0;// 已移除大小

            var firstItem = _queue.Dequeue();
            Array.Copy(firstItem.Buffer, _readOffset, result, alreadyRemoveCount, firstItem.BytesTransferred - _readOffset);
            alreadyRemoveCount += firstItem.BytesTransferred - _readOffset;

            _collectSaea.Invoke(firstItem);

            while (_queue.Count > 0)
            {
                var item = _queue.Dequeue();

                Array.Copy(item.Buffer, 0, result, alreadyRemoveCount, item.BytesTransferred);
                alreadyRemoveCount += item.BytesTransferred;

                _collectSaea.Invoke(item);
            }

            ResetField();

            return result;
        }

        /// <summary>
        /// <para>从缓冲区读取一行，但不超过maxSize个字符，并以字节数组的形式返回结果，</para>
        /// <para>如果无法从缓冲区读取一整行将返回空的字节数组</para>
        /// <para>此函数无法报告错误；返回空的字节数组可能意味着当前没有可供读取的数据，或者发生错误。</para>
        /// </summary>
        /// <returns></returns>
        public byte[] ReadLine()
        {
            if (_newlineIndex == -1)// 读取前没有调用CanReadLine
            {
                return Read(IndexOf(10) + 1);
            }
            else
            {
                return Read(_newlineIndex + 1);
            }
        }

        /// <summary>
        ///  从数组头部开始搜索指定的byte对象，并返回读取缓存中第一个匹配项的索引，索引基于_readOffset偏移。
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public long IndexOf(byte value)
        {
            if (_queue.Count == 0)// 未找到
            {
                return -1L;
            }

            long resultIndex;

            var firstItem = _queue.Peek();
            resultIndex = Array.IndexOf(firstItem.Buffer, value, _readOffset, firstItem.BytesTransferred - _readOffset);
            if (resultIndex != -1)// 找到
            {
                return resultIndex - _readOffset;
            }

            int tempIndex;
            resultIndex = firstItem.BytesTransferred - _readOffset;// 已查找元素个数

            foreach (var item in _queue)
            {
                if (item != firstItem)// 不是第一个
                {
                    tempIndex = Array.IndexOf(item.Buffer, value, 0, item.BytesTransferred);
                    if (tempIndex != -1)// 找到
                    {
                        return resultIndex + tempIndex;
                    }
                    else// 未找到
                    {
                        resultIndex += item.BytesTransferred;
                    }
                }
            }
            return -1;
        }

        #endregion 公开方法

        #region 内部方法

        internal ReadBuffer(Action<SocketAsyncEventArgs> collectSaea, int receiveBufferSize)
        {
            _queue = new Queue<SocketAsyncEventArgs>();
            _collectSaea = collectSaea;
            _receiveBufferSize = receiveBufferSize;
        }

        /// <summary>
        /// 缓存RecvSaea
        /// </summary>
        /// <param name="e"></param>
        internal void CacheSaea(SocketAsyncEventArgs e)
        {
            if (_readBufferMaxCount <= 0 || _queue.Count < _readBufferMaxCount)
            {
                _queue.Enqueue(e);
                _bytesAvailable += e.BytesTransferred;
            }
            else
            {
                _collectSaea.Invoke(e);
                string errorMsg = "读取缓冲区溢出，缓冲区中的字节数大于" + ReadBufferMaxLength;
                throw new Exception(errorMsg);
            }
        }

        /// <summary>
        /// 回收所有 RecvSaea 并重置字段
        /// </summary>
        internal void Clear()
        {
            try
            {
                while (_queue.Count > 0)
                {
                    var e = _queue.Dequeue();
                    _collectSaea.Invoke(e);
                }
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                ResetField();
            }
        }

        /// <summary>
        /// 重置字段
        /// </summary>
        private void ResetField()
        {
            _bytesAvailable = 0;
            _readOffset = 0;
            _newlineIndex = -1;
        }
        #endregion
    }
}
