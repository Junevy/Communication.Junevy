using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Core.Parsing;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
using Communication.ModBus.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Communication.Modbus.TCP
{
    public sealed class ModbusTCP : IModbus
    {
        private Socket? socket;
        private NetworkStream? stream;
        private PipeReader? reader;

        private readonly ILogger<ModbusTCP> logger;
        private readonly IResponseParser responseParser;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long lastTimestamp;
        private bool disposed;

        public ModbusTCPConfig Config { get; private set; }
        public bool IsConnected => !disposed && (socket?.Connected ?? false);
        public ModbusProtocolType ProtocolType => ModbusProtocolType.TCP;

        public ModbusTCP(ModbusTCPConfig config)
            : this(config, NullLogger<ModbusTCP>.Instance, new TcpProtocolParser())
        {
        }

        public ModbusTCP(ModbusTCPConfig config, ILogger<ModbusTCP> logger)
            : this(config, logger, new TcpProtocolParser())
        {
        }

        public ModbusTCP(ModbusTCPConfig config, ILogger<ModbusTCP> logger, IResponseParser responseParser)
        {
            ArgumentNullException.ThrowIfNull(config);
            this.Config = config;
            this.logger = logger ?? NullLogger<ModbusTCP>.Instance;
            this.responseParser = responseParser ?? new TcpProtocolParser();

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        }

        private void InitialSocket(ModbusTCPConfig config)
        {
            socket!.ReceiveTimeout = config.ReadTimeOut;
            socket!.SendTimeout = config.WriteTimeOut;
        }

        private void InitialStream()
        {
            stream = new NetworkStream(socket!, ownsSocket: true);
            reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
                pool: MemoryPool<byte>.Shared,
                bufferSize: 2048,
                minimumReadSize: 1,
                leaveOpen: false));
        }

        public bool Connect()
        {
            ThrowIfDisposed();

            if (!ModbusHelper.VerifyAddress(Config.Address) || !ModbusHelper.VerifyPort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            try
            {
                InitialSocket(this.Config);
                var result = socket!.BeginConnect(Config.Address, Config.Port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(Config.ConnectTimeout, true);
                if (success)
                {
                    socket.EndConnect(result);
                    InitialStream();
                    logger.LogDebug(" [Connect] Connected to {Address}:{Port}.", Config.Address, Config.Port);
                    return true;
                }
                else
                {
                    socket.Close();
                    logger.LogWarning(" [Connect] Connection timed out: {Timeout}ms.", Config.ConnectTimeout);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Connect] Connection failed.");
                throw;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            ThrowIfDisposed();

            if (!ModbusHelper.VerifyAddress(Config.Address) || !ModbusHelper.VerifyPort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            using var cancellationToken = new CancellationTokenSource(Config.ConnectTimeout);

            try
            {
                await socket!.ConnectAsync(Config.Address, Config.Port, cancellationToken.Token);
                InitialStream();
                logger.LogDebug(" [ConnectAsync] Connected to {Address}:{Port}.", Config.Address, Config.Port);
                return true;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(" [ConnectAsync] Connection timed out: {Timeout}ms.", Config.ConnectTimeout);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [ConnectAsync] Connection failed.");
                throw;
            }
        }

        /// <summary>
        /// Disconnects the socket. The instance can be reconnected afterwards.
        /// </summary>
        public void Disconnect()
        {
            try
            {
                reader?.Complete();
                reader = null;
                stream?.Dispose();
                stream = null;

                if (socket?.Connected ?? false)
                {
                    socket.Disconnect(false);
                }
                socket?.Close();
                socket = null;

                logger.LogDebug(" [Disconnect] Disconnected from {Address}:{Port}.", Config.Address, Config.Port);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Disconnect] Disconnect failed.");
                throw;
            }
        }

        public ModbusResult<byte[]> Request(ModbusRequest request)
        {
            if (!CheckConnection())
            {
                logger.LogWarning(" [Request] Not connected.");
                return ModbusResult<byte[]>.Fail(" [Request] Not connected.");
            }

            if (!ModbusHelper.CheckRequest(request))
            {
                logger.LogWarning(" [Request] Invalid request: {@Request}.", request);
                return ModbusResult<byte[]>.Fail(" [Request] Invalid request.");
            }

            requestLock.Wait();

            try
            {
                logger.LogDebug(" [Request] Sending request: {@Request}.", request);
                var sendResult = Send(request);
                if (!sendResult)
                {
                    logger.LogWarning(" [Request] Send failed.");
                    return ModbusResult<byte[]>.Fail(" [Request] Send failed.");
                }

                return Read(request);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Request] Request failed.");
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private bool Send(ModbusRequest request)
        {
            try
            {
                var frame = ModbusHelper.BuildRequestFrame(request);
                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    int sent = socket!.Send(frame, totalSent, frame.Length - totalSent, SocketFlags.None);
                    totalSent += sent;
                    logger.LogDebug(" [Send] Total sent: {Total}, current: {Current}.", totalSent, sent);

                    if (sent == 0)
                        return false;
                }
                logger.Tx("ModbusTCP", frame, stopwatch, ref lastTimestamp);
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger.LogError(" [Send] Send timed out.");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Send] Send failed.");
                throw;
            }
        }

        /// <summary>
        /// Synchronous read using the PipeReader. Unlike the previous sync-over-async pattern,
        /// this reads directly without wrapping async calls.
        /// </summary>
        private ModbusResult<byte[]> Read(ModbusRequest request)
        {
            try
            {
                using var cts = new CancellationTokenSource(Config.ReadTimeOut);

                // Read MBAP header (6 bytes) into a small stack buffer
                Span<byte> headerSpan = stackalloc byte[6];
                int headerRead = 0;
                while (headerRead < 6)
                {
                    if (cts.IsCancellationRequested)
                        return ModbusResult<byte[]>.Fail(" [Read] Read timeout.");
                    int read = socket!.Receive(headerSpan.Slice(headerRead, 6 - headerRead));
                    if (read == 0)
                        return ModbusResult<byte[]>.Fail(" [Read] Connection closed by remote.");
                    headerRead += read;
                }

                ushort pduLength = BinaryExtensions.ToUshort(headerSpan[5], headerSpan[4]);
                if (pduLength < 1 || pduLength > 253)
                {
                    throw new ModbusException(ModbusErrorCode.InvalidData,
                        $" [Read] Invalid PDU length: {pduLength}.");
                }

                // Single allocation: header (6) + PDU (pduLength)
                int totalLength = 6 + pduLength;
                byte[] fullFrame = new byte[totalLength];

                // Copy header from stack
                headerSpan.CopyTo(fullFrame.AsSpan(0, 6));

                // Read PDU directly into the combined buffer
                var pduSpan = fullFrame.AsSpan(6, pduLength);
                int pduRead = 0;
                while (pduRead < pduLength)
                {
                    if (cts.IsCancellationRequested)
                        return ModbusResult<byte[]>.Fail(" [Read] Read timeout.");
                    int read = socket!.Receive(pduSpan.Slice(pduRead));
                    if (read == 0)
                        return ModbusResult<byte[]>.Fail(" [Read] Connection closed by remote.");
                    pduRead += read;
                }

                logger.Rx("ModbusTCP", fullFrame, stopwatch, ref lastTimestamp);

                var parsed = responseParser.ParseResponse(fullFrame, request);
                return parsed.IsSuccess
                    ? ModbusResult<byte[]>.Success(parsed.Data.ToArray())
                    : ModbusResult<byte[]>.Fail(parsed.ErrorMessage ?? " [Read] Parse error.", fullFrame);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger.LogError(" [Read] Read timed out.");
                return ModbusResult<byte[]>.Fail(" [Read] Read timeout.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Read] Read failed.");
                throw;
            }
        }

        public async Task<ModbusResult<byte[]>> RequestAsync(ModbusRequest request, CancellationToken cancellationToken = default)
        {
            if (!CheckConnection())
            {
                logger.LogWarning(" [RequestAsync] Not connected.");
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Not connected.");
            }

            if (!ModbusHelper.CheckRequest(request))
            {
                logger.LogWarning(" [RequestAsync] Invalid request: {@Request}.", request);
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Invalid request.");
            }

            try
            {
                await requestLock.WaitAsync(cancellationToken);
                logger.LogDebug(" [RequestAsync] Sending request: {@Request}.", request);
                var sendResult = await SendAsync(request, cancellationToken);
                if (!sendResult)
                {
                    logger.LogWarning(" [RequestAsync] Send failed.");
                    return ModbusResult<byte[]>.Fail(" [RequestAsync] Send failed.");
                }

                return await ReadAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(" [RequestAsync] Request cancelled.");
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Request cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [RequestAsync] Request failed.");
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async ValueTask<bool> SendAsync(ModbusRequest requestObj, CancellationToken cancellationToken = default)
        {
            try
            {
                var frame = ModbusHelper.BuildRequestFrame(requestObj);
                using var sendTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeoutToken.CancelAfter(Config.WriteTimeOut);
                sendTimeoutToken.Token.ThrowIfCancellationRequested();

                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    var sent = await socket!.SendAsync(frame.AsMemory(totalSent), sendTimeoutToken.Token);
                    totalSent += sent;
                    logger.LogDebug(" [SendAsync] Total sent: {Total}, current: {Current}.", totalSent, sent);

                    if (sent == 0)
                        return false;
                }
                logger.Tx("ModbusTCP", frame, stopwatch, ref lastTimestamp);
                return true;
            }
            catch (OperationCanceledException)
            {
                logger.LogError(" [SendAsync] Send timed out.");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [SendAsync] Send failed.");
                throw;
            }
        }

        private async ValueTask<ModbusResult<byte[]>> ReadAsync(ModbusRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Config.ReadTimeOut);

                // Read MBAP header
                ReadOnlySequence<byte> headerSeq = await ReadExactAsync(6, cts.Token);
                Span<byte> headerSpan = stackalloc byte[6];
                headerSeq.CopyTo(headerSpan);

                // Parse PDU length
                ushort pduLength = BinaryExtensions.ToUshort(headerSpan[5], headerSpan[4]);
                if (pduLength < 1 || pduLength > 253)
                {
                    reader!.AdvanceTo(headerSeq.End);
                    throw new ModbusException(ModbusErrorCode.InvalidData,
                        $" [ReadAsync] Invalid PDU length: {pduLength}.");
                }

                // Read PDU
                int totalLength = 6 + pduLength;
                logger.LogDebug(" [ReadAsync] Total length: {Length}.", totalLength);
                ReadOnlySequence<byte> fullSeq = await ReadExactAsync(totalLength, cts.Token);

                ReadOnlyMemory<byte> data = fullSeq.ToArray();
                logger.Rx("ModbusTCP", data.Span, stopwatch, ref lastTimestamp);
                reader!.AdvanceTo(fullSeq.End);

                var parsed = responseParser.ParseResponse(data, request);
                return parsed.IsSuccess
                    ? ModbusResult<byte[]>.Success(parsed.Data.ToArray())
                    : ModbusResult<byte[]>.Fail(parsed.ErrorMessage ?? " [ReadAsync] Parse error.", data.ToArray());
            }
            catch (OperationCanceledException)
            {
                logger.LogError(" [ReadAsync] Read timed out.");
                return ModbusResult<byte[]>.Fail(" [ReadAsync] Read timeout.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [ReadAsync] Read failed.");
                throw;
            }
        }

        private async ValueTask<ReadOnlySequence<byte>> ReadExactAsync(int length, CancellationToken cancellationToken = default)
        {
            try
            {
                while (true)
                {
                    ReadResult result = await reader!.ReadAsync(cancellationToken);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (buffer.Length >= length)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.GetPosition(length));
                        return buffer.Slice(0, length);
                    }

                    if (result.IsCompleted)
                    {
                        reader.AdvanceTo(buffer.Start, buffer.End);
                        throw new EndOfStreamException(" [ReadExactAsync] Connection closed unexpectedly.");
                    }

                    // Haven't received enough data yet — mark as examined but not consumed
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning(" [ReadExactAsync] Read cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [ReadExactAsync] Read failed.");
                throw;
            }
        }

        public bool CheckConnection() => IsConnected;

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModbusTCP));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            reader?.Complete();
            reader = null;
            stream?.Dispose();
            stream = null;
            socket?.Dispose();
            socket = null;
            requestLock?.Dispose();
            logger.LogDebug(" [Dispose] ModbusTCP disposed.");
        }
    }
}
