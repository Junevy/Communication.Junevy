using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using System.IO.Ports;

namespace Communication.ModBus.ModBusRTU
{
    public sealed class ModBusRTUMaster(ISerilog logger, ModBusRTUConfig config) : IModBus
    {
        private bool disposed;
        private readonly ISerilog logger = logger;

        public bool IsConnected => serialPort.IsOpen;
        private readonly SerialPort serialPort = new();
        private readonly SemaphoreSlim requestLock = new(1, 1);
        public ModBusRTUConfig Config { get; private set; } = config ?? throw new ArgumentNullException(nameof(config) + "is null!");

        public bool Connect()
        {
            ThrowIfDisposed();
            if (serialPort.IsOpen)
                Disconnect();

            ConfigurePort();

            try
            {
                serialPort.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return false;
            }
            return true;
        }

        private void ConfigurePort()
        {
            if (IsConnected) return;

            try
            {
                serialPort.PortName = Config.PortName;
                serialPort.BaudRate = Config.BaudRate;
                serialPort.Parity = Config.Parity;
                serialPort.DataBits = Config.DataBits;
                serialPort.StopBits = Config.StopBits;
                serialPort.DtrEnable = Config.DtrEnable;
                serialPort.RtsEnable = Config.RtsEnable;

                serialPort.ReadTimeout = Config.ReadTimeOut;
                serialPort.WriteTimeout = Config.WriteTimeOut;
            }
            catch (Exception ex)
            {

            }

        }

        public void Disconnect()
        {
            try
            {
                if (!IsConnected)
                    serialPort.Close();
                this.serialPort.Dispose();
            }
            catch { }
        }

        #region Read Coils _ 01H
        public Result<ushort[]> Read(byte slaveID, ushort functionCode, ushort start, ushort length)
        {
            return ReadAsync(slaveID, functionCode, start, length).GetAwaiter().GetResult();
        }

        public async Task<Result<ushort[]>> ReadAsync(byte slaveID, ushort functionCode, ushort start, ushort length, CancellationToken token = default)
        {
            if (!IsConnected)
                return Result<ushort[]>.Fail("Port not open.");

            if (length == 0)
                return Result<ushort[]>.Fail("Read length can not be 0!");

            byte[] request = ModBusHelper.BuildReadFrame(slaveID, (byte)functionCode, start, length);

            return await ExecuteReadAsync(request, slaveID, (byte)functionCode, response =>
            {
                return ModBusResponseParser.ParseReadBytes(response, slaveID, functionCode, length);
            }, token);

        }
        #endregion

        private async Task<Result<T>> ExecuteReadAsync<T>(byte[] request, byte slaveID, byte functionCode,
            Func<byte[], Result<T>> parser, CancellationToken token = default)
        {
            ThrowIfDisposed();
            string lastError = string.Empty;

            try
            {
                await requestLock.WaitAsync(token);

                // 重试
                for (int i = 0; i <= Config.RetryCount; i++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        this.serialPort.DiscardInBuffer();  // 清除串口区缓存
                        this.serialPort.DiscardOutBuffer();

                        // 异步处理
                        await Task.Run(() => serialPort.Write(request, 0, request.Length), token);
                        var receiveResult = await ReceiveFrameAsync(slaveID, functionCode, token);

                        if (!receiveResult.IsSuccess)
                        {
                            lastError = "Try parse frame error!";
                            continue;
                        }
                        return parser(receiveResult.Data!);
                    }
                    catch (TimeoutException)
                    {
                        //logger.Tx("Write timeout");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }
                return Result<T>.Fail("Failed after retries.");
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async Task<Result<byte[]>> ReceiveFrameAsync(byte slaveID, byte funcCode, CancellationToken token)
        {
            var buffer = new List<byte>(256);
            var temp = new byte[256];

            try
            {
                while (true)
                {
                    // 响应外部的全局取消请求（比如用户点击了停止按钮）
                    token.ThrowIfCancellationRequested();

                    int count = 0;
                    try
                    {
                        // 使用 Task.Run 包装同步 Read 方法
                        // 这样既能真正响应串口的 2000ms ReadTimeout，又能在等待期间不阻塞主线程
                        count = await Task.Run(() => this.serialPort.Read(temp, 0, temp.Length), token);
                    }
                    catch (TimeoutException)
                    {
                        // 当 2000ms 没有读到任何数据时，原生 Read 方法会抛出 TimeoutException
                        // 对于 Modbus RTU 来说，帧超时通常意味着读取失败或从站没响应
                        return Result<byte[]>.Fail("读取从站超时 (2000ms)");
                    }

                    if (count <= 0) continue;

                    buffer.AddRange(temp.AsSpan(0, count));

                    // 尝试解析 Modbus 帧
                    if (ModBusRTUFrame.TryExtractResponseFrame(buffer, slaveID, funcCode, out var frame))
                    {
                        return Result<byte[]>.Success(frame);
                    }

                    // 如果还没凑够一帧，稍微等待一下给Slave一点缓冲时间
                    await Task.Delay(Config.IntervalTime, token);
                }
            }
            catch (OperationCanceledException oex)
            {
                // 捕获到全局 token 被取消（比如用户主动停止通讯）
                if (token.IsCancellationRequested)
                    throw;
                return Result<byte[]>.Fail(oex.ToString());
            }
            catch (Exception e)
            {
                return Result<byte[]>.Fail(e.ToString());
            }
        }

        public void Dispose()
        {
            if (serialPort.IsOpen)
                Disconnect();
            serialPort.Dispose();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
