using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using Communication.ModBus.Core;
using System.IO.Ports;

namespace Communication.ModBus.ModBusRTU
{
    public sealed class ModBusRTUMaster(ModBusRTUConfig config) : IModBus
    {
        private bool disposed = false;
        private readonly ISerilog? logger = Serilogger.Instance;

        public bool IsConnected => serialPort.IsOpen;
        // public bool AutoReceiveAfterSend {get; set;} = true;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.RTU;
        private readonly SerialPort serialPort = new();
        private readonly SemaphoreSlim requestLock = new(1, 1);
        /// <summary>
        /// ModBus 配置参数。
        /// </summary>
        /// <exception cref="ArgumentNullException">当配置参数为 null 时，抛出异常。</exception>
        public ModBusRTUConfig Config { get; private set; } = config ?? throw new ArgumentNullException(nameof(config) + "is null!");


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
                return false;
            }
            return true;
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
                logger?.Error("Configure port failed: {@Config}, {Exception}", Config, ex.Message);
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
                logger?.Error("Disconnect port failed: {@Config}, {Exception}", Config, ex.Message);
                throw;
            }
        }

        public Rx<byte[]> Request(Tx tx)
        {
            return RequestAsync(tx).GetAwaiter().GetResult();
        }

        public async Task<Rx<byte[]>> RequestAsync(Tx tx, CancellationToken token = default)
        {
            logger?.Information("Build Execute Tx: {@Tx}", tx);

            if (!IsConnected)
            {
                logger?.Warning("Port not open: {Config.PortName}.", Config.PortName);
                return Rx<byte[]>.Fail("Port not open");
            }

            if (tx.Length == 0)
            {
                logger?.Warning("Read length can not be 0!");
                return Rx<byte[]>.Fail("Read length can not be 0!");
            }

            if ( (tx.FunctionCode>= ModBusFunctionCode.WriteCoils) && tx.Data== null)
            {
                logger?.Warning("Data can not be null When function code is 0x05, 0x06, 0x0F, 0x10, function code: {Tx.FunctionCode}.", tx.FunctionCode);
                return Rx<byte[]>.Fail("Data can not be null When function code is 0x05, 0x06, 0x0F, 0x10!");
            }

            try
            {
                byte[] request = ModBusTools.BuildTxFrame(tx, ProtocolType);
                return await ExecuteAsync(request, tx, response =>
                {
                    return ModBusRxParser.ParseRx(response, tx);
                }, token);
            }
            catch (Exception ex)
            {
                logger?.Error( "Execute request error!", ex);
                return Rx<byte[]>.Fail(ex.Message);
            }
        }

        /// <summary>
        /// 执行请求。
        /// </summary>
        /// <param name="request">请求数据。</param>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="parser">响应解析器。</param>
        /// <param name="token">取消令牌。</param>
        /// <returns>执行结果。</returns>
        private async Task<Rx<T>> ExecuteAsync<T>(byte[] request, Tx tx, Func<byte[], Rx<T>> parser, CancellationToken token = default)
        {
            ThrowIfDisposed();

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

                        var receiveResult = await ReceiveAsync(tx, token);

                        if (!receiveResult.IsSuccess)
                        {
                            logger?.Warning("Try parse frame error: {@Rx.Data}", receiveResult.Data);
                            continue;
                        }
                        logger?.Information("Try parse frame success: {@Rx.Data}", receiveResult.Data);
                        return parser(receiveResult.Data!);
                    }
                    catch (TimeoutException)
                    {
                        logger?.Error("Write timeout: {Config.WriteTimeOut}", Config.WriteTimeOut);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger?.Error("Execute request error!", ex);
                    }
                }
                return Rx<T>.Fail("Failed after retries.");
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async Task<Rx<byte[]>> ReceiveAsync(Tx tx, CancellationToken token = default)
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
                        //实现串口的 2000ms ReadTimeout，且在等待期间不阻塞主线程
                        count = await Task.Run(() => this.serialPort.Read(temp, 0, temp.Length), token);
                    }
                    catch (TimeoutException)
                    {
                        logger?.Error("Read timeout: {Config.ReadTimeOut}", Config.ReadTimeOut);
                        return Rx<byte[]>.Fail($"Read savle timeout: ({Config.ReadTimeOut}ms)");
                    }

                    if (count <= 0) continue;

                    buffer.AddRange(temp.AsSpan(0, count));

                    // 尝试解析 Modbus 帧
                    if (ModBusRxParser.TryExtractRxFrame(buffer, (byte)tx.SlaveId, (byte)tx.FunctionCode, out var frame))
                    {
                        logger?.Information("Try parse frame success: {@Rx.Data}", frame);
                        return Rx<byte[]>.Success(frame);
                    }

                    // 如果还没凑够一帧，等待一下给Slave一点缓冲时间
                    logger?.Debug("Wait {Config.IntervalTime}ms for next frame...", Config.IntervalTime);
                    await Task.Delay(Config.IntervalTime, token);
                }
            }
            catch (OperationCanceledException oex)
            {
                logger?.Error("Receive response error: {oex.Message}", oex.Message);
                // 捕获到全局 token 被取消（比如主动停止通讯）
                if (token.IsCancellationRequested)
                    throw;
                return Rx<byte[]>.Fail(oex.ToString());
            }
            catch (Exception e)
            {
                logger?.Error("Receive response error: {e.Message}", e.Message);
                return Rx<byte[]>.Fail(e.ToString());
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
