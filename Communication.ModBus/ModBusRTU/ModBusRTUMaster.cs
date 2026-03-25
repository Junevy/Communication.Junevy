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
            logger.Debug("test");
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
            serialPort.PortName = Config.PortName;
            serialPort.BaudRate = Config.BaudRate;
            serialPort.Parity = Config.Parity;
            serialPort.DataBits = Config.DataBits;
            serialPort.StopBits = Config.StopBits;
            serialPort.DtrEnable = Config.DtrEnable;
            serialPort.RtsEnable = Config.RtsEnable;

            serialPort.ReadTimeout = Timeout.Infinite;
            serialPort.WriteTimeout = Timeout.Infinite;
        }

        public void Disconnect()
        {
            try
            {
                if (!IsConnected)
                    serialPort.Close();
            }
            catch { }
        }

        #region Read Coils _ 01H
        public Result<bool[]> ReadCoils(byte slaveID, ushort start, ushort length)
        {
            return ReadCoilsAsync(slaveID, start, length).GetAwaiter().GetResult();
        }

        public async Task<Result<bool[]>> ReadCoilsAsync(byte slaveID, ushort start, ushort length, CancellationToken token = default)
        {
            if (!IsConnected)
                return Result<bool[]>.Fail("Port not open.");

            if (length == 0)
                return Result<bool[]>.Fail("Read length can not be 0!");

            byte[] request = ModBusHelper.BuildReadFrame(slaveID, 0x01, start, length);

            return await ExecuteReadAsync(request, slaveID, 0x01, response =>
            {
                return ModBusResponseParser.ParseReadCoils(response, slaveID, 0x01, length);
            }, token);
        }

        private async Task<Result<T>> ExecuteReadAsync<T>(byte[] request, byte slaveID, byte functionCode,
            Func<byte[], Result<T>> parser, CancellationToken token = default)
        {
            ThrowIfDisposed();
            string lastError = string.Empty;

            var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            writeTimeout.CancelAfter(Config.WriteTimeOut);

            try
            {
                await requestLock.WaitAsync(token);

                // 重试
                for (int i = 0; i < Config.RetryCount; i++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        this.serialPort.DiscardInBuffer();  // 清除串口区缓存
                        this.serialPort.DiscardOutBuffer();

                        // 异步处理
                        await serialPort.BaseStream.WriteAsync(request, writeTimeout.Token);
                        await serialPort.BaseStream.FlushAsync(token);
                        var receiveResult = await ReceiveFrameAsync(slaveID, functionCode, token);

                        if (!receiveResult.IsSuccess)
                        {
                            lastError = "Try parse frame error!";
                            continue;
                        }
                        return parser(receiveResult.Data!);
                    }
                    catch (OperationCanceledException)
                    {
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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(Config.ReadTimeOut);

            var timeoutToken = timeoutCts.Token;
            var buffer = new List<byte>(256);
            var temp = new byte[256];

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    var count = await this.serialPort.BaseStream.ReadAsync(temp, 0, temp.Length, timeoutToken);

                    if (count <= 0) continue;

                    buffer.AddRange(temp.AsSpan(0, count));
                    //buffer.AddRange(temp.AsSpan(0, count).ToArray());

                    if (ModBusRTUFrame.TryExtractResponseFrame(buffer, slaveID, funcCode, out var frame))
                        return Result<byte[]>.Success(frame);

                    await Task.Delay(Config.IntervalTime, token);   // 等待帧
                }
            }
            catch (OperationCanceledException oex)
            {
                if (token.IsCancellationRequested)
                    throw;
                return Result<byte[]>.Fail(oex.ToString());
            }
            catch (Exception e)
            {
                return Result<byte[]>.Fail(e.ToString());
            }
        }
        #endregion

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
