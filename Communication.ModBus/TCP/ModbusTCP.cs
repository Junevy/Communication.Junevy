using Communication.Modbus.Common;
using Communication.Modbus.Core;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
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

        private readonly ISerilog? logger = Serilogger.Instance;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        public ModbusTCPConfig Config { get; private set; }
        public bool IsConnected => socket.Connected;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.TCP;
        
        public ModbusTCP(ModbusTCPConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            this.Config = config;
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
                    return true;
                }
                else
                {
                    socket.Close();
                    logger?.Warning(" [Connect] Connect socket has been timeout: {Config.ConnectTimeout}ms", Config.ConnectTimeout);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error(" [Connect] Connect socket has been occured an error : {ex.Message}", ex.Message);
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

                return true;
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning(" [ConnectAsync] Connect socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error(" [ConnectAsync] Connect socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }
        
        public void Disconnect()
        {
            try
            {
                socket.Disconnect(false);
            }
            catch (Exception ex)
            {
                logger?.Error(" [Disconnect] Close socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }
        

        public ModbusResult<byte[]> Request(ModbusRequest tx)
        {
            if (!CheckConnection())
                return ModbusResult<byte[]>.Fail(" [Request] Not connected.");

            if (!ModbusHelper.CheckRequest(tx))
                return ModbusResult<byte[]>.Fail(" [Request] Invalid Tx.");

            requestLock.Wait();

            try
            {
                var sendResult = Send(tx);
                return !sendResult ? ModbusResult<byte[]>.Fail(" [Request] Send error.") : Read(tx);
            }
            catch (Exception ex)
            {
                logger?.Error(" [Request] Request socket has been occured an error : {ex.Message}", ex.Message);
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
                var frame = ModbusHelper.BuildRequestFrame(tx);

                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    int sent = socket.Send(frame, totalSent, frame.Length - totalSent, SocketFlags.None);
                    if (sent == 0)
                        return false;
                    totalSent += sent;
                }
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger?.Warning(" [Send] Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error(" [Send] Send socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }
        private ModbusResult<byte[]> Read(ModbusRequest tx)
        {
            return Task.Run(async () => await ReadAsync(tx)).GetAwaiter().GetResult();
        }

        public async Task<ModbusResult<byte[]>> RequestAsync(ModbusRequest tx, CancellationToken cancellationToken = default)
        {
            if (!CheckConnection())
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Not connected.");

            if (!ModbusHelper.CheckRequest(tx))
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Invalid Tx.");

            try
            {
                await requestLock.WaitAsync(cancellationToken);

                var sendResult = await SendAsync(tx, cancellationToken);
                if (!sendResult)
                    return ModbusResult<byte[]>.Fail(" [RequestAsync] Send error.");

                return await ReadAsync(tx, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning(" [RequestAsync] Request socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail(" [RequestAsync] Request timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error(" [RequestAsync] Request socket has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async ValueTask<bool> SendAsync(ModbusRequest tx, CancellationToken cancellationToken = default)
        {
            try
            {
                var frame = ModbusHelper.BuildRequestFrame(tx);

                using var sendTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeoutToken.CancelAfter(Config.WriteTimeOut);
                sendTimeoutToken.Token.ThrowIfCancellationRequested();

                // Ensure all bytes are sent
                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    var sent = await socket.SendAsync(frame.AsMemory(totalSent), sendTimeoutToken.Token);

                    if (sent == 0)
                        return false;

                    totalSent += sent;
                }
                return true;
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning(" [SendAsync] Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error(" [SendAsync] Send socket has been occured an error : {ex.Message}", ex.Message);
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
                ReadOnlySequence<byte> fullSeq = await ReadExactAsync(totalLength, cts.Token);

                // 处理数据
                ReadOnlyMemory<byte> data = fullSeq.ToArray();
                reader.AdvanceTo(fullSeq.End); // 消费完整的报文

                var parsed = ResponseParser.ParseResponse(data, tx);
                return parsed.IsSuccess
                    ? ModbusResult<byte[]>.Success(parsed.Data.ToArray())
                    : ModbusResult<byte[]>.Fail(parsed?.ErrorMessage ?? " [ReadAsync] Parse error", data.ToArray());
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning(" [ReadAsync] Read socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail(" [ReadAsync] Read timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error(" [ReadAsync] Read socket has been occured an error : {ex.Message}", ex.Message);
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
            catch (OperationCanceledException ex)
            {
                logger?.Warning(" [ReadExactAsync] The connection has been closed.");
                throw;
            }
            catch (Exception ex)
            {
                logger?.Error(" [ReadExactAsync] ReadExact has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        public bool CheckConnection() => IsConnected;

        public void Dispose()
        {
            socket?.Dispose();
            requestLock?.Dispose();
            logger?.Information(" [ModbusTCP] ModbusTCPMaster has been disposed.");
        }

    }
}