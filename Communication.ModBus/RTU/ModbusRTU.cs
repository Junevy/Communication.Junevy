using Communication.Modbus.Common;
using Communication.Modbus.Utils;
using Communication.Modbus.Core;
using System.IO.Ports;

namespace Communication.Modbus.RTU
{
    public sealed class ModbusRTU(ModbusRTUConfig config) : IModbus
    {
        private bool disposed;
        private readonly ISerilog? logger = Serilogger.Instance;

        public bool IsConnected => serialPort.IsOpen;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.RTU;
        private readonly SerialPort serialPort = new();
        private readonly SemaphoreSlim requestLock = new(1, 1);

        /// <summary>
        /// ModBus 配置参数。
        /// </summary>
        /// <exception cref="ModbusException">当配置参数为 null 时，抛出异常。</exception>
        public ModbusRTUConfig Config { get; } = config ??
                                                 throw new ModbusException(ModbusErrorCode.InvalidValue,
                                                     nameof(config) + "is null!");

        // 连接功能，连接至com口
        public bool Connect()
        {
            ThrowIfDisposed();

            if (serialPort.IsOpen)
            {
                Disconnect();
            }

            InitialConnection();

            try
            {
                serialPort.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
            return true;
        }

        /// <summary>
        /// 异步连接功能，实际使用Task.run方式运行
        /// </summary>
        /// <returns>异步任务，任务完成时返回连接成功</returns>
        public Task<bool> ConnectAsync()
        {
            // SerialPort doesn't have an async Open method, so we run it on a thread pool thread
            return Task.Run(Connect);
        }

        private void InitialConnection()
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
                logger?.Error(" [InitialConnection] Configure port failed: {@Config}, {Exception}", Config, ex.Message);
                throw;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (!IsConnected)
                    serialPort.Close();
                this.serialPort.Dispose();
                disposed = true;
            }
            catch (Exception ex)
            {
                logger?.Error(" [Disconnect] Config failed: {@Config}, {Exception}", Config, ex.Message);
                throw;
            }
        }

        public ModbusResult<byte[]> Request(ModbusRequest request)
        {
            logger?.Information(" [Request] Build Execute Tx: {@Tx}", request);

            if (!IsConnected)
            {
                logger?.Warning(" [Request] Port not open: {Config.PortName}.", Config.PortName);
                return ModbusResult<byte[]>.Fail(" [Request] Port not open");
            }

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<byte[]>.Fail(" [Request] Invalid Tx.", request.Data);

            try
            {
                requestLock.Wait();
                var sendResult = Send(request);

                if (!sendResult) return ModbusResult<byte[]>.Fail(" [Request] Send frame occured an error.");

                return Read(request);
            }
            catch (Exception ex)
            {
                logger?.Error(" [Request] Execute request error!", ex);
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private bool Send(ModbusRequest request)
        {
            ThrowIfDisposed();

            try
            {
                byte[] requestFrame = ModbusHelper.BuildRequestFrame(request);

                // 清除串口区缓存
                this.serialPort.DiscardInBuffer();
                this.serialPort.DiscardOutBuffer();

                this.serialPort.Write(requestFrame, 0, requestFrame.Length);
                return true;
            }
            catch (TimeoutException)
            {
                logger?.Error(" [Send] Write timeout: {Config.WriteTimeOut}", Config.WriteTimeOut);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error(" [Send] Execute request error!", ex);
                throw;
            }
        }

        private ModbusResult<byte[]> Read(ModbusRequest request)
        {
            var pool = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
            int readCounts = 0;

            try
            {
                while (true)
                {
                    int readBytes = 0;
                    try
                    {
                        readBytes = this.serialPort.Read(pool, readCounts, pool.Length - readCounts);
                        readCounts += readBytes;
                    }
                    catch (TimeoutException)
                    {
                        logger?.Error(" [Read] Read timeout: {Config.ReadTimeOut}", Config.ReadTimeOut);
                        return ModbusResult<byte[]>.Fail($" [Read] Read savle timeout: ({Config.ReadTimeOut}ms)");
                    }

                    logger?.Debug(" [Read] Read count: {Count}", readCounts);

                    if (readCounts < 5) continue;
                    var memory = pool.AsMemory(0, readCounts);

                    // 尝试解析 Modbus 帧
                    var parseResult = ResponseParser.ParseResponse(memory, request);

                    // 正常帧
                    if (parseResult.IsSuccess)
                    {
                        logger?.Information(" [Read] Try parse frame success: {@Rx.Data}", parseResult.Data);
                        if (parseResult.Data.Length <= 0)
                        {
                            logger?.Warning(" [Read] Parse frame failed, because the data length < 0 : {@Rx.Data}", parseResult.Data);
                            throw new ModbusException(ModbusErrorCode.InvalidData, " [Read] Parse frame failed.");
                        }
                        return ModbusResult<byte[]>.Success(parseResult.Data.ToArray());
                    }

                    // 等待读取完整的一帧
                    logger?.Debug(" [Read] Wait {Config.IntervalTime}ms for next frame...", Config.IntervalTime);
                    Thread.Sleep(Config.IntervalTime);
                }
            }
            catch (Exception e)
            {
                logger?.Error(" [Read] Receive response error: {e.Message}", e.Message);
                throw;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(pool);
            }
        }

        /// <summary>
        /// 执行请求
        /// </summary>
        /// <param name="request">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果</returns>
        public async Task<ModbusResult<byte[]>> RequestAsync(ModbusRequest request, CancellationToken token = default)
        {
            logger?.Information(" [RequestAsync] Build Execute Tx: {@Tx}", request);

            if (!IsConnected)
            {
                logger?.Warning(" [RequestAsync] Port not open: {Config.PortName}.", Config.PortName);
                return ModbusResult<byte[]>.Fail("Port not open");
            }

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Invalid Tx.", request.Data);

            try
            {
                await requestLock.WaitAsync(token);
                var sendResult = await SendAsync(request, token);

                if (!sendResult) return ModbusResult<byte[]>.Fail(" [RequestAsync] Send frame occured an error.");

                return await ReadAsync(request, token);
            }
            catch (Exception ex)
            {
                logger?.Error(" [RequestAsync] Execute request error!", ex);
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        /// <summary>
        /// 执行请求
        /// </summary>
        /// <param name="request">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果。</returns>
        private async Task<bool> SendAsync(ModbusRequest request, CancellationToken token = default)
        {
            ThrowIfDisposed();

            try
            {
                byte[] requestFrame = ModbusHelper.BuildRequestFrame(request);
                token.ThrowIfCancellationRequested();

                // 清除串口区缓存
                this.serialPort.DiscardInBuffer();
                this.serialPort.DiscardOutBuffer();

                // 异步处理
                await Task.Run(() => serialPort.Write(requestFrame, 0, requestFrame.Length), token);
                return true;
            }
            catch (TimeoutException)
            {
                logger?.Error(" [SendAsync] Write timeout: {Config.WriteTimeOut}", Config.WriteTimeOut);
                return false;
            }
            catch (OperationCanceledException)
            {
                logger?.Error(" [SendAsync] Send Task Cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error(" [SendAsync] Execute request error!", ex);
                throw;
            }
        }

        /// <summary>
        /// 读取响应
        /// </summary>
        /// <param name="request">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果</returns>
        private async Task<ModbusResult<byte[]>> ReadAsync(ModbusRequest request, CancellationToken token = default)
        {
            var pool = System.Buffers.ArrayPool<byte>.Shared.Rent(256);
            int readCounts = 0;

            try
            {
                var readTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeoutToken.CancelAfter(Config.ReadTimeOut);
                while (true)
                {
                    readTimeoutToken.Token.ThrowIfCancellationRequested();
                    int readBytes = 0;
                    try
                    {
                        //实现串口的 2000ms ReadTimeout，且在等待期间不阻塞主线程
                        readBytes = await Task.Run(() => this.serialPort.Read(pool, readCounts, pool.Length - readCounts), readTimeoutToken.Token);
                        readCounts += readBytes;
                    }
                    catch (TimeoutException)
                    {
                        logger?.Error(" [ReadAsync] Read timeout: {Config.ReadTimeOut}", Config.ReadTimeOut);
                        return ModbusResult<byte[]>.Fail($" [ReadAsync] Read savle timeout: ({Config.ReadTimeOut}ms)");
                    }

                    if (readCounts < 5) continue;
                    var memory = pool.AsMemory(0, readCounts);

                    // 尝试解析 Modbus 帧
                    var parseResult = ResponseParser.ParseResponse(memory, request);
                    if (parseResult.IsSuccess)
                    {
                        logger?.Information(" [ReadAsync] Try parse frame success: {@Rx.Data}", parseResult.Data);
                        if (parseResult.Data.Length <= 0)
                        {
                            logger?.Warning(" [ReadAsync] Parse frame failed, because the data length < 0 : {@Rx.Data}", parseResult.Data);
                            throw new InvalidOperationException(" [ReadAsync] Parse frame failed.");
                        }

                        logger?.Information(" [ReadAsync] Try parse frame success: {@Rx.Data}", parseResult.Data);
                        return ModbusResult<byte[]>.Success(parseResult.Data.Span.ToArray());
                    }

                    // 等待读取完整的一帧
                    logger?.Debug(" [ReadAsync] Wait {Config.IntervalTime}ms for next frame...", Config.IntervalTime);
                    await Task.Delay(Config.IntervalTime, token);
                }
            }
            catch (OperationCanceledException oex)
            {
                logger?.Error(" [ReadAsync] Receive response error: {oex.Message}", oex.Message);
                return ModbusResult<byte[]>.Fail(oex.ToString());
            }
            catch (Exception e)
            {
                logger?.Error(" [ReadAsync] Receive response error: {e.Message}", e.Message);
                throw;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(pool);
            }
        }

        public void Dispose()
        {
            if (serialPort.IsOpen)
                Disconnect();
            serialPort.Dispose();
            disposed = true;
        }

        /// <summary>
        /// 检查是否已处置。
        /// </summary>
        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }
}
