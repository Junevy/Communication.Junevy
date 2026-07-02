using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Framing;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Core.Parsing;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO.Ports;

namespace Communication.Modbus.RTU
{
    public sealed class ModbusRTU : IModbus
    {
        private bool disposed;
        private readonly ILogger<ModbusRTU> logger;
        private readonly IResponseParser responseParser;
        private readonly IModbusFrameBuilder frameBuilder;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long lastTimestamp;

        public bool IsConnected => !disposed && serialPort.IsOpen;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.RTU;
        private readonly SerialPort serialPort = new();
        private readonly SemaphoreSlim requestLock = new(1, 1);

        /// <summary>
        /// Modbus RTU configuration.
        /// </summary>
        public ModbusRTUConfig Config { get; }

        public ModbusRTU(ModbusRTUConfig config)
            : this(config, NullLogger<ModbusRTU>.Instance, new RtuProtocolParser())
        {
        }

        public ModbusRTU(ModbusRTUConfig config, ILogger<ModbusRTU> logger)
            : this(config, logger, new RtuProtocolParser())
        {
        }

        public ModbusRTU(ModbusRTUConfig config, ILogger<ModbusRTU> logger, IResponseParser responseParser)
            : this(config, logger, responseParser, new ModbusFrameBuilder())
        {
        }

        public ModbusRTU(
            ModbusRTUConfig config,
            ILogger<ModbusRTU> logger,
            IResponseParser responseParser,
            IModbusFrameBuilder frameBuilder)
        {
            this.Config = config
                ?? throw new ModbusException(ModbusErrorCode.InvalidValue, nameof(config) + " is null!");
            this.logger = logger ?? NullLogger<ModbusRTU>.Instance;
            this.responseParser = responseParser ?? new RtuProtocolParser();
            this.frameBuilder = frameBuilder ?? new ModbusFrameBuilder();
        }

        /// <summary>
        /// Opens the serial port connection.
        /// </summary>
        public bool Connect()
        {
            ThrowIfDisposed();

            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }

            InitialConnection();

            try
            {
                serialPort.Open();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Connect] Failed to open port {PortName}.", Config.PortName);
                throw;
            }
            return true;
        }

        /// <summary>
        /// Asynchronously opens the serial port connection.
        /// </summary>
        public Task<bool> ConnectAsync()
        {
            // SerialPort does not have an async Open method, so we use Task.Run.
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
                logger.LogError(ex, " [InitialConnection] Configure port failed: {@Config}.", Config);
                throw;
            }
        }

        /// <summary>
        /// Closes the serial port connection. The instance can be reconnected afterwards.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                    logger.LogDebug(" [Disconnect] Port {PortName} closed.", Config.PortName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Disconnect] Failed to close port {PortName}.", Config.PortName);
                throw;
            }
        }

        public ModbusResult<byte[]> Request(ModbusRequest request)
        {
            logger.LogInformation(" [Request] Executing request: {@Request}", request);

            if (!IsConnected)
            {
                logger.LogWarning(" [Request] Port not open: {PortName}.", Config.PortName);
                return ModbusResult<byte[]>.Fail(" [Request] Port not open.");
            }

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<byte[]>.Fail(" [Request] Invalid request.", request.Data);

            try
            {
                requestLock.Wait();
                var sendResult = Send(request);

                if (!sendResult) return ModbusResult<byte[]>.Fail(" [Request] Send frame failed.");

                return Read(request);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Request] Request execution failed.");
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
                request.ProtocolType = ProtocolType;
                var requestFrame = System.Buffers.ArrayPool<byte>.Shared.Rent(ModbusFrameBuilder.MaxRtuAduLength);
                try
                {
                    if (!frameBuilder.TryWriteRequestFrame(request, requestFrame, out int bytesWritten))
                        return false;

                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();

                    serialPort.Write(requestFrame, 0, bytesWritten);
                    logger.Tx("ModbusRTU", new ArraySegment<byte>(requestFrame, 0, bytesWritten).ToArray(), stopwatch, ref lastTimestamp);
                    return true;
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(requestFrame);
                }
            }
            catch (TimeoutException)
            {
                logger.LogError(" [Send] Write timeout: {Timeout}ms.", Config.WriteTimeOut);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Send] Send failed.");
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
                        readBytes = serialPort.Read(pool, readCounts, pool.Length - readCounts);
                        readCounts += readBytes;
                    }
                    catch (TimeoutException)
                    {
                        logger.LogError(" [Read] Read timeout: {Timeout}ms.", Config.ReadTimeOut);
                        return ModbusResult<byte[]>.Fail($" [Read] Read slave timeout: ({Config.ReadTimeOut}ms).");
                    }

                    logger.LogDebug(" [Read] Bytes received: {Count}.", readCounts);

                    if (readCounts < 5) continue;
                    var memory = pool.AsMemory(0, readCounts);

                    var parseResult = responseParser.ParseResponse(memory, request);

                    if (parseResult.IsSuccess)
                    {
                        if (parseResult.Data.Length <= 0)
                        {
                            logger.LogWarning(" [Read] Parsed frame has zero length.");
                            throw new ModbusException(ModbusErrorCode.InvalidData, " [Read] Parsed frame has zero length.");
                        }
                        logger.Rx("ModbusRTU", parseResult.Data.Span, stopwatch, ref lastTimestamp);
                        return ModbusResult<byte[]>.Success(parseResult.Data.ToArray());
                    }

                    logger.Rx("ModbusRTU", parseResult.Data.Span, stopwatch, ref lastTimestamp);
                    logger.LogDebug(" [Read] Waiting {Interval}ms for next frame...", Config.IntervalTime);
                    Thread.Sleep(Config.IntervalTime);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Read] Receive response failed.");
                throw;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(pool);
            }
        }

        public async Task<ModbusResult<byte[]>> RequestAsync(ModbusRequest request, CancellationToken token = default)
        {
            logger.LogInformation(" [RequestAsync] Executing request: {@Request}", request);

            if (!IsConnected)
            {
                logger.LogWarning(" [RequestAsync] Port not open: {PortName}.", Config.PortName);
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Port not open.");
            }

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Invalid request.", request.Data);

            var lockTaken = false;
            try
            {
                await requestLock.WaitAsync(token);
                lockTaken = true;
                var sendResult = await SendAsync(request, token);

                if (!sendResult) return ModbusResult<byte[]>.Fail(" [RequestAsync] Send frame failed.");

                return await ReadAsync(request, token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [RequestAsync] Request execution failed.");
                throw;
            }
            finally
            {
                if (lockTaken)
                    requestLock.Release();
            }
        }

        private async Task<bool> SendAsync(ModbusRequest request, CancellationToken token = default)
        {
            ThrowIfDisposed();

            try
            {
                request.ProtocolType = ProtocolType;
                int frameLength = frameBuilder.GetRequestFrameLength(request);
                byte[] requestFrame = System.Buffers.ArrayPool<byte>.Shared.Rent(frameLength);
                token.ThrowIfCancellationRequested();

                try
                {
                    if (!frameBuilder.TryWriteRequestFrame(request, requestFrame, out int bytesWritten))
                        return false;

                    serialPort.DiscardInBuffer();
                    serialPort.DiscardOutBuffer();

                    await serialPort.BaseStream.WriteAsync(requestFrame, 0, bytesWritten, token);
                    logger.Tx("ModbusRTU", new ArraySegment<byte>(requestFrame, 0, bytesWritten).ToArray(), stopwatch, ref lastTimestamp);
                    return true;
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(requestFrame);
                }
            }
            catch (TimeoutException)
            {
                logger.LogError(" [SendAsync] Write timeout: {Timeout}ms.", Config.WriteTimeOut);
                return false;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(" [SendAsync] Send cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [SendAsync] Send failed.");
                throw;
            }
        }

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
                        readBytes = await Task.Run(() => serialPort.Read(pool, readCounts, pool.Length - readCounts), readTimeoutToken.Token);
                        readCounts += readBytes;
                    }
                    catch (TimeoutException)
                    {
                        logger.LogError(" [ReadAsync] Read timeout: {Timeout}ms.", Config.ReadTimeOut);
                        return ModbusResult<byte[]>.Fail($" [ReadAsync] Read slave timeout: ({Config.ReadTimeOut}ms).");
                    }

                    if (readCounts < 5) continue;
                    var memory = pool.AsMemory(0, readCounts);

                    var parseResult = responseParser.ParseResponse(memory, request);
                    if (parseResult.IsSuccess)
                    {
                        if (parseResult.Data.Length <= 0)
                        {
                            logger.LogWarning(" [ReadAsync] Parsed frame has zero length.");
                            throw new InvalidOperationException(" [ReadAsync] Parsed frame has zero length.");
                        }

                        logger.Rx("ModbusRTU", parseResult.Data.Span, stopwatch, ref lastTimestamp);
                        return ModbusResult<byte[]>.Success(parseResult.Data.Span.ToArray());
                    }

                    logger.Rx("ModbusRTU", parseResult.Data.Span, stopwatch, ref lastTimestamp);
                    logger.LogDebug(" [ReadAsync] Waiting {Interval}ms for next frame...", Config.IntervalTime);
                    await Task.Delay(Config.IntervalTime, token);
                }
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(ex, " [ReadAsync] Read cancelled.");
                return ModbusResult<byte[]>.Fail(ex.ToString());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [ReadAsync] Receive response failed.");
                throw;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(pool);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try
            {
                if (serialPort.IsOpen)
                    serialPort.Close();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, " [Dispose] Error closing serial port.");
            }

            serialPort.Dispose();
            requestLock.Dispose();
            logger.LogDebug(" [Dispose] ModbusRTU disposed.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModbusRTU));
        }
    }
}
