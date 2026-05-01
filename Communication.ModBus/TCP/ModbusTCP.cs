using Communication.Modbus.Common;
using Communication.Modbus.Core;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Communication.Modbus.TCP
{
    public sealed class ModbusTCP : IModbus
    {
        private readonly Socket socket;
        private NetworkStream stream;
        private PipeReader reader;

        private readonly ILogger<ModbusTCP> logger;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        public ModbusTCPConfig Config { get; private set; }
        public bool IsConnected => socket.Connected;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.TCP;
        
        public ModbusTCP(ModbusTCPConfig config) : this(config, NullLogger<ModbusTCP>.Instance)
        {
        }

        public ModbusTCP(ModbusTCPConfig config, ILogger<ModbusTCP> logger)
        {
            ArgumentNullException.ThrowIfNull(config);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            this.Config = config;
            this.logger = logger ?? NullLogger<ModbusTCP>.Instance;
        }

        private void InitialSocket(ModbusTCPConfig config)
        {
            socket.ReceiveTimeout = config.ReadTimeOut;
            socket.SendTimeout = config.WriteTimeOut;
        }

        private void InitialStream()
        {
            stream = new NetworkStream(socket, ownsSocket: true);
            reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
                pool: MemoryPool<byte>.Shared,
                bufferSize: 2048,
                minimumReadSize: 1,
                leaveOpen: false));
        }

        public bool Connect()
        {
            if (!ModbusHelper.VerifyAddress(Config.Address) || !ModbusHelper.VerifyPort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            try
            {
                InitialSocket(this.Config);
                var result = socket.BeginConnect(Config.Address, Config.Port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(Config.ConnectTimeout, true);
                if (success)
                {
                    socket.EndConnect(result);
                    InitialStream();
                    logger.LogDebug(" [Connect] Connect socket successfully to {Address}:{Port}", Config.Address, Config.Port);
                    return true;
                }
                else
                {
                    socket.Close();
                    logger.LogWarning(" [Connect] Connect socket has been timeout: {Config.ConnectTimeout}ms", Config.ConnectTimeout);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(" [Connect] Connect socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            if (!ModbusHelper.VerifyAddress(Config.Address) || !ModbusHelper.VerifyPort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            using var cancellationToken = new CancellationTokenSource(Config.ConnectTimeout);

            try
            {
                await socket.ConnectAsync(Config.Address, Config.Port, cancellationToken.Token);
                InitialStream();
                logger.LogDebug(" [ConnectAsync] Connect socket successfully to {Address}:{Port}", Config.Address, Config.Port);
                return true;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(" [ConnectAsync] Connect socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(" [ConnectAsync] Connect socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }
        
        public void Disconnect()
        {
            try
            {
                socket.Disconnect(false);
                logger.LogDebug(" [Disconnect] The Connection has been closed: {Address}:{Port}", Config.Address, Config.Port);
            }
            catch (Exception ex)
            {
                logger.LogError(" [Disconnect] Close socket error : {ex.Message}", ex.Message);
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
                logger.LogWarning(" [Request] Invalid request: {@request}", request);
                return ModbusResult<byte[]>.Fail(" [Request] Invalid request.");
            }

            requestLock.Wait();

            try
            {
                logger.LogDebug(" [Request] Sending request: {@request}", request);
                var sendResult = Send(request);
                if (!sendResult)
                {
                    logger.LogWarning(" [Request] Send error");
                    return ModbusResult<byte[]>.Fail(" [Request] Send error.");
                }

                return Read(request);
            }
            catch (Exception ex)
            {
                logger.LogError(" [Request] Request socket error: {ex.Message}", ex.Message);
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private bool Send(ModbusRequest tx)
        {
            try
            {
                var request = ModbusHelper.BuildRequestFrame(tx);
                int totalSent = 0;
                while (totalSent < request.Length)
                {
                    int sent = socket.Send(request, totalSent, request.Length - totalSent, SocketFlags.None);
                    totalSent += sent;
                    logger.LogDebug(" [Send] Total sent: {totalSent}, current sent: {sent}", totalSent, sent);

                    if (sent == 0)
                        return false;
                }
                logger.Tx("MobudTCP", request);
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger.LogError(" [Send] Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(" [Send] Send socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }
        private ModbusResult<byte[]> Read(ModbusRequest request)
        {
            return Task.Run(async () => await ReadAsync(request)).GetAwaiter().GetResult();
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
                logger.LogWarning(" [RequestAsync] Invalid request: {@request}", request);
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Invalid request.");
            }

            try
            {
                await requestLock.WaitAsync(cancellationToken);
                logger.LogDebug(" [RequestAsync] Sending request: {@request}", request);
                var sendResult = await SendAsync(request, cancellationToken);
                if (!sendResult)
                {
                    logger.LogWarning(" [RequestAsync] Send error");
                    return ModbusResult<byte[]>.Fail(" [RequestAsync] Send error.");
                }

                return await ReadAsync(request, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(" [RequestAsync] Request timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Request timeout.");
            }
            catch (Exception ex)
            {
                logger.LogError(" [RequestAsync] Request socket has been occured an error : {ex.Message}", ex.Message);
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
                var request = ModbusHelper.BuildRequestFrame(requestObj);
                using var sendTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeoutToken.CancelAfter(Config.WriteTimeOut);
                sendTimeoutToken.Token.ThrowIfCancellationRequested();

                // Ensure all bytes are sent
                int totalSent = 0;
                while (totalSent < request.Length)
                {
                    var sent = await socket.SendAsync(request.AsMemory(totalSent), sendTimeoutToken.Token);
                    totalSent += sent;
                    logger.LogDebug(" [SendAsync] Total sent: {totalSent}, current sent: {sent}", totalSent, sent);

                    if (sent == 0)
                        return false;
                }
                logger.Tx("ModbusTCP", request);
                return true;
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(" [SendAsync] Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(" [SendAsync] Send socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        private async ValueTask<ModbusResult<byte[]>> ReadAsync(ModbusRequest tx, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Config.ReadTimeOut);

                // MBAP Header
                ReadOnlySequence<byte> headerSeq = await ReadExactAsync(6, cts.Token);
                Span<byte> headerSpan = stackalloc byte[6];
                headerSeq.CopyTo(headerSpan);

                // 解析 PDU 长度 
                ushort pduLength = BinaryExtensions.ToUshort(headerSpan[5], headerSpan[4]);
                if (pduLength < 1 || pduLength > 253)
                {
                    reader.AdvanceTo(headerSeq.End);    // 异常数据，推进 Reader，避免死循环
                    throw new ModbusException(ModbusErrorCode.InvalidData,
                        $" [ReadAsync] Invalid PDU length: {pduLength}");
                }

                // MBAP + PDU
                int totalLength = 6 + pduLength;
                logger.LogDebug(" [ReadAsync] Total length: {@totalLength}", totalLength);
                ReadOnlySequence<byte> fullSeq = await ReadExactAsync(totalLength, cts.Token);

                // 处理数据
                ReadOnlyMemory<byte> data = fullSeq.ToArray();
                logger.Rx("ModbusTCP", data.Span);
                reader.AdvanceTo(fullSeq.End); // 消费完整的报文

                var parsed = ResponseParser.ParseResponse(data, tx);
                return parsed.IsSuccess
                    ? ModbusResult<byte[]>.Success(parsed.Data.ToArray())
                    : ModbusResult<byte[]>.Fail(parsed?.ErrorMessage ?? " [ReadAsync] Parse error", data.ToArray());
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(" [ReadAsync] Read socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail(" [ReadAsync] Read timeout.");
            }
            catch (Exception ex)
            {
                logger.LogError(" [ReadAsync] Read socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        private async ValueTask<ReadOnlySequence<byte>> ReadExactAsync(int length, CancellationToken cancellationToken = default)
        {
            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync(cancellationToken);
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

                    reader.AdvanceTo(buffer.Start, buffer.End); // 未消费，等待更多数据
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogError(" [ReadExactAsync] The connection has been closed.");
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(" [ReadExactAsync] ReadExact has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        public bool CheckConnection() => IsConnected;

        public void Dispose()
        {
            socket?.Dispose();
            requestLock?.Dispose();
            logger.LogDebug(" [ModbusTCP] ModbusTCPMaster has been disposed.");
        }

    }
}