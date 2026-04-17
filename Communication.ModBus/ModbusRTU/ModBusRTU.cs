using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using Communication.ModBus.Core;
using System.IO.Ports;

namespace Communication.ModBus.ModbusRTU
{
    public sealed class ModBusRTU(ModBusRTUConfig config) : IModbus
    {
        private bool disposed = false;
        private readonly ISerilog? logger = Serilogger.Instance;

        public bool IsConnected => serialPort.IsOpen;
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

        public Rx Request(Tx tx)
        {
            logger?.Information("Build Execute Tx: {@Tx}", tx);

            if (!IsConnected)
            {
                logger?.Warning("Port not open: {Config.PortName}.", Config.PortName);
                return Rx.Fail("Port not open");
            }

            if (!ModBusTools.CheckTx(tx))
                return Rx.Fail("Invalid Tx.", tx.Data);

            try
            {
                requestLock.Wait();
                var sendResult = Send(tx);

                if (!sendResult) return Rx.Fail("Send frame occured an error.");

                return Read(tx);
            }
            catch (Exception ex)
            {
                logger?.Error("Execute request error!", ex);
                return Rx.Fail(ex.Message);
            }
            finally
            {
                requestLock.Release();
            }
        }

        private bool Send(Tx tx)
        {
            ThrowIfDisposed();

            try
            {
                byte[] request = ModBusTools.BuildTxFrame(tx);

                // 清除串口区缓存
                this.serialPort.DiscardInBuffer();  
                this.serialPort.DiscardOutBuffer();

                this.serialPort.Write(request, 0, request.Length);
                return true;
            }
            catch (TimeoutException)
            {
                logger?.Error("Write timeout: {Config.WriteTimeOut}", Config.WriteTimeOut);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error("Execute request error!", ex);
                return false;
            }
        }

        private Rx Read(Tx tx)
        {
            var buffer = new List<byte>(256);
            var temp = new byte[256];

            try
            {
                while (true)
                {
                    int count = 0;
                    try
                    {
                        count = this.serialPort.Read(temp, 0, temp.Length);
                    }
                    catch (TimeoutException)
                    {
                        logger?.Error("Read timeout: {Config.ReadTimeOut}", Config.ReadTimeOut);
                        return Rx.Fail($"Read savle timeout: ({Config.ReadTimeOut}ms)");
                    }

                    if (count <= 0) continue;

                    buffer.AddRange(temp.AsSpan(0, count));

                    // 尝试解析 Modbus 帧
                    var parseResult = ModbusRxParser.ParseRx(buffer.ToArray(), tx);
                    if (parseResult.IsSuccess)
                    {
                        logger?.Information("Try parse frame success: {@Rx.Data}", parseResult.Data);
                        return Rx.Success(parseResult.Data ?? throw new InvalidOperationException("Parse frame failed."));
                    }

                    // 等待读取完整的一帧
                    logger?.Debug("Wait {Config.IntervalTime}ms for next frame...", Config.IntervalTime);
                    Thread.Sleep(Config.IntervalTime);
                }
            }
            catch (Exception e)
            {
                logger?.Error("Receive response error: {e.Message}", e.Message);
                return Rx.Fail(e.ToString());
            }
        }

        /// <summary>
        /// 执行请求
        /// </summary>
        /// <param name="tx">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果</returns>
        public async Task<Rx> RequestAsync(Tx tx, CancellationToken token = default)
        {
            logger?.Information("Build Execute Tx: {@Tx}", tx);

            if (!IsConnected)
            {
                logger?.Warning("Port not open: {Config.PortName}.", Config.PortName);
                return Rx.Fail("Port not open");
            }

            if (!ModBusTools.CheckTx(tx))
                return Rx.Fail("Invalid Tx.", tx.Data);

            try
            {
                await requestLock.WaitAsync(token);
                var sendResult = await SendAsync(tx, token);

                if (!sendResult) return Rx.Fail("Send frame occured an error.");

                return await ReadAsync(tx, token);
            }
            catch (Exception ex)
            {
                logger?.Error("Execute request error!", ex);
                return Rx.Fail(ex.Message);
            }
            finally
            {
                requestLock.Release();
            }
        }

        /// <summary>
        /// 执行请求
        /// </summary>
        /// <param name="tx">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果。</returns>
        private async Task<bool> SendAsync(Tx tx, CancellationToken token = default)
        {
            ThrowIfDisposed();

            try
            {
                byte[] request = ModBusTools.BuildTxFrame(tx);

                token.ThrowIfCancellationRequested();
                // 清除串口区缓存
                this.serialPort.DiscardInBuffer();  
                this.serialPort.DiscardOutBuffer();

                // 异步处理
                await Task.Run(() => serialPort.Write(request, 0, request.Length), token);
                return true;
            }
            catch (TimeoutException)
            {
                logger?.Error("Write timeout: {Config.WriteTimeOut}", Config.WriteTimeOut);
                return false;
            }
            catch (OperationCanceledException)
            {
                logger?.Error("Send Task Cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error("Execute request error!", ex);
                return false;
            }
        }

        /// <summary>
        /// 读取响应
        /// </summary>
        /// <param name="tx">ModBus请求帧</param>
        /// <param name="token">取消令牌</param>
        /// <returns>执行结果</returns>
        private async Task<Rx> ReadAsync(Tx tx, CancellationToken token = default)
        {
            var buffer = new List<byte>(256);
            var temp = new byte[256];

            try
            {
                while (true)
                {
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
                        return Rx.Fail($"Read savle timeout: ({Config.ReadTimeOut}ms)");
                    }

                    if (count <= 0) continue;

                    buffer.AddRange(temp.AsSpan(0, count));

                    // 尝试解析 Modbus 帧
                    var parseResult = ModbusRxParser.ParseRx(buffer.ToArray(), tx);
                    if (parseResult.IsSuccess)
                    {
                        logger?.Information("Try parse frame success: {@Rx.Data}", parseResult.Data);
                        return Rx.Success(parseResult.Data ?? throw new InvalidOperationException("Parse frame failed."));
                    }

                    // 等待读取完整的一帧
                    logger?.Debug("Wait {Config.IntervalTime}ms for next frame...", Config.IntervalTime);
                    await Task.Delay(Config.IntervalTime, token);
                }
            }
            catch (OperationCanceledException oex)
            {
                logger?.Error("Receive response error: {oex.Message}", oex.Message);
                return Rx.Fail(oex.ToString());
            }
            catch (Exception e)
            {
                logger?.Error("Receive response error: {e.Message}", e.Message);
                return Rx.Fail(e.ToString());
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
