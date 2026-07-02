using Communication.Modbus.Core.Framing;
using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Core.Parsing;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;

namespace Communication.Modbus.TCP
{
    public sealed class ModbusTCP : IModbus
    {
        private Socket? socket;
        private readonly ILogger<ModbusTCP> logger;
        private readonly IResponseParser responseParser;
        private readonly IModbusFrameBuilder frameBuilder;
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
            : this(config, logger, responseParser, new ModbusFrameBuilder())
        {
        }

        public ModbusTCP(
            ModbusTCPConfig config,
            ILogger<ModbusTCP> logger,
            IResponseParser responseParser,
            IModbusFrameBuilder frameBuilder)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            Config = config;
            this.logger = logger ?? NullLogger<ModbusTCP>.Instance;
            this.responseParser = responseParser ?? new TcpProtocolParser();
            this.frameBuilder = frameBuilder ?? new ModbusFrameBuilder();
            socket = CreateSocket();
        }

        public bool Connect()
        {
            ThrowIfDisposed();

            if (!ModbusHelper.VerifyAddress(Config.Address) || !ModbusHelper.VerifyPort(Config.Port))
                return false;

            ResetSocket();

            try
            {
                var result = socket!.BeginConnect(Config.Address, Config.Port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(Config.ConnectTimeout, true);
                if (!success)
                {
                    socket.Dispose();
                    socket = null;
                    logger.LogWarning(" [Connect] Connection timed out: {Timeout}ms.", Config.ConnectTimeout);
                    return false;
                }

                socket.EndConnect(result);
                logger.LogDebug(" [Connect] Connected to {Address}:{Port}.", Config.Address, Config.Port);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, " [Connect] Connection failed.");
                throw;
            }
        }

        public Task<bool> ConnectAsync()
        {
            return Task.Run(Connect);
        }

        public void Disconnect()
        {
            try
            {
                if (socket?.Connected ?? false)
                    socket.Disconnect(false);

                socket?.Dispose();
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
                if (!Send(request))
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

        public async Task<ModbusResult<byte[]>> RequestAsync(
            ModbusRequest request,
            CancellationToken cancellationToken = default)
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

            var lockTaken = false;
            try
            {
                await requestLock.WaitAsync(cancellationToken);
                lockTaken = true;

                if (!await SendAsync(request, cancellationToken))
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
                if (lockTaken)
                    requestLock.Release();
            }
        }

        public bool CheckConnection() => IsConnected;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            socket?.Dispose();
            socket = null;
            requestLock.Dispose();
            logger.LogDebug(" [Dispose] ModbusTCP disposed.");
        }

        private bool Send(ModbusRequest request)
        {
            byte[]? frame = null;
            try
            {
                request.ProtocolType = ProtocolType;
                frame = ArrayPool<byte>.Shared.Rent(ModbusFrameBuilder.MaxTcpAduLength);
                if (!frameBuilder.TryWriteRequestFrame(request, frame, out int bytesWritten))
                    return false;

                int totalSent = 0;
                while (totalSent < bytesWritten)
                {
                    int sent = socket!.Send(frame, totalSent, bytesWritten - totalSent, SocketFlags.None);
                    totalSent += sent;
                    logger.LogDebug(" [Send] Total sent: {Total}, current: {Current}.", totalSent, sent);

                    if (sent == 0)
                        return false;
                }

                logger.Tx("ModbusTCP", new ArraySegment<byte>(frame, 0, bytesWritten).ToArray(), stopwatch, ref lastTimestamp);
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
            finally
            {
                if (frame != null)
                    ArrayPool<byte>.Shared.Return(frame);
            }
        }

        private async ValueTask<bool> SendAsync(ModbusRequest request, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Send(request);
            }, cancellationToken);
        }

        private ModbusResult<byte[]> Read(ModbusRequest request)
        {
            byte[]? frame = null;
            try
            {
                frame = ArrayPool<byte>.Shared.Rent(ModbusFrameBuilder.MaxTcpAduLength);
                var headerResult = ReceiveExact(frame, 0, 6);
                if (!headerResult.IsSuccess)
                    return headerResult;

                ushort pduLength = BinaryExtensions.ToUshort(frame[5], frame[4]);
                if (pduLength < 1 || pduLength > 254)
                {
                    throw new ModbusException(ModbusErrorCode.InvalidData,
                        $" [Read] Invalid PDU length: {pduLength}.");
                }

                int totalLength = 6 + pduLength;
                var payloadResult = ReceiveExact(frame, 6, pduLength);
                if (!payloadResult.IsSuccess)
                    return payloadResult;

                var data = new ReadOnlyMemory<byte>(frame, 0, totalLength);
                logger.Rx("ModbusTCP", data.Span, stopwatch, ref lastTimestamp);

                var parsed = responseParser.ParseResponse(data, request);
                return parsed.IsSuccess
                    ? ModbusResult<byte[]>.Success(parsed.Data.ToArray())
                    : ModbusResult<byte[]>.Fail(parsed.ErrorMessage ?? " [Read] Parse error.", data.ToArray());
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
            finally
            {
                if (frame != null)
                    ArrayPool<byte>.Shared.Return(frame);
            }
        }

        private async ValueTask<ModbusResult<byte[]>> ReadAsync(
            ModbusRequest request,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Read(request);
            }, cancellationToken);
        }

        private ModbusResult<byte[]> ReceiveExact(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = socket!.Receive(buffer, offset + totalRead, count - totalRead, SocketFlags.None);
                if (read == 0)
                    return ModbusResult<byte[]>.Fail(" [Read] Connection closed by remote.");

                totalRead += read;
            }

            return ModbusResult<byte[]>.Success(Array.Empty<byte>());
        }

        private void ResetSocket()
        {
            socket?.Dispose();
            socket = CreateSocket();
            socket.ReceiveTimeout = Config.ReadTimeOut;
            socket.SendTimeout = Config.WriteTimeOut;
        }

        private static Socket CreateSocket()
            => new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModbusTCP));
        }
    }
}
