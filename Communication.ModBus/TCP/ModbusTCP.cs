using Communication.Modbus.Common;
using Communication.Modbus.Core;
using Communication.Modbus.Utils;
using Communication.Modbus.Core;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Communication.Modbus.TCP
{
    public sealed class ModbusTCP : IModbus
    {
        private readonly Socket socket;
        private readonly NetworkStream stream;
        private readonly PipeReader reader;

        private readonly ISerilog? logger = Serilogger.Instance;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        public ModbusTCPConfig Config { get; private set; }
        public bool IsConnected => socket.Connected;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.TCP;

        public ModbusTCP(ModbusTCPConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            stream = new NetworkStream(socket, ownsSocket: true);
            reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
                pool: MemoryPool<byte>.Shared,
                bufferSize: 2048,
                minimumReadSize: 1,
                leaveOpen: false));

            this.Config = config;
        }

        private void InitialSocket(ModbusTCPConfig config)
        {
            socket.ReceiveTimeout = config.ReadTimeOut;
            socket.SendTimeout = config.WriteTimeOut;
        }

        public bool Connect()
        {
            if (!ModbusTools.ValidateAddress(Config.Address) || !ModbusTools.ValidatePort(Config.Port))
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
                    return true;
                }
                else
                {
                    socket.Close();
                    logger?.Warning("Connect socket has been timeout: {Config.ConnectTimeout}ms", Config.ConnectTimeout);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error("Connect socket has been occured an error : {ex.Message}", ex.Message);
                return false;
            }
        }

        public async Task<bool> ConnectAsync()
        {
            if (!ModbusTools.ValidateAddress(Config.Address) || !ModbusTools.ValidatePort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            using var cancellationToken = new CancellationTokenSource(Config.ConnectTimeout);

            try
            {
                await socket.ConnectAsync(Config.Address, Config.Port, cancellationToken.Token);
                return true;
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Connect socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error("Connect socket has been occured an error : {ex.Message}", ex.Message);
                return false;
            }
        }
        
        public void Disconnect()
        {
            try
            {
                socket.Disconnect(true);
            }
            catch (Exception ex)
            {
                logger?.Warning("Close socket has been occured an error : {ex.Message}", ex.Message);
            }
        }
        
        public ModbusResult<byte[]> Request(ModbusTx tx)
        {
            if (!CheckConnection())
                return ModbusResult<byte[]>.Fail("Not connected.");

            if (!ModbusTools.CheckTx(tx))
                return ModbusResult<byte[]>.Fail("Invalid Tx.");

            requestLock.Wait();

            try
            {
                var sendResult = Send(tx);
                if (!sendResult)
                    return ModbusResult<byte[]>.Fail("Send error.");

                return Read(tx);
            }
            catch (Exception ex)
            {
                logger?.Error("Request socket has been occured an error : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Request error.");
            }
            finally
            {
                requestLock.Release();
            }
        }
        private bool Send(ModbusTx tx)
        {
            try
            {
                var frame = ModbusTools.BuildTxFrame(tx);

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
                logger?.Warning("Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error("Send socket has been occured an error : {ex.Message}", ex.Message);
                return false;
            }
        }
        private ModbusResult<byte[]> Read(ModbusTx tx)
        {
            try
            {
                // Calculate PDU length and read PDU
                ReadOnlySequence<byte> mbap = ReadExact(ModbusParams.MBAP_LENGTH);
                var lowByte = mbap.FirstSpan[5];
                var highByte = mbap.FirstSpan[4];
                ushort pduLength = (ushort) (BitExtentions.ToUshort(lowByte, highByte) - 1);
                ReadOnlySequence<byte> rest = ReadExact(pduLength);

                // Rent memory
                ushort totalLength = (ushort) (ModbusParams.MBAP_LENGTH + pduLength);
                using var owner = MemoryPool<byte>.Shared.Rent(totalLength);
                var target = owner.Memory[..totalLength];

                // Merge MBAP and PDU
                mbap.CopyTo(target.Span);
                rest.CopyTo(target.Span[ModbusParams.MBAP_LENGTH..]);
                var parsed = ModbusRxParser.ParseRx(target, tx);

                if (parsed.IsSuccess)
                    return ModbusResult<byte[]>.Success(parsed.Data.Span.ToArray());
                return ModbusResult<byte[]>.Fail(parsed?.ErrorMessage ?? "Parse error.");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger?.Warning("Receive socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Receive timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Receive socket has been occured an error : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Receive error.");
            }
        }
        private ReadOnlySequence<byte> ReadExact(int length)
        {
            while (true)
            {
                reader.TryRead(out var result);
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (buffer.Length >= length)
                {
                    ReadOnlySequence<byte> slice = buffer.Slice(0, length);
                    reader.AdvanceTo(slice.End, buffer.End);
                    return slice;
                }

                if (result.IsCompleted)
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    throw new EndOfStreamException($"Connection closed after {buffer.Length} bytes, expected {length}.");
                }
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        public async Task<ModbusResult<byte[]>> RequestAsync(ModbusTx tx, CancellationToken cancellationToken = default)
        {
            if (!CheckConnection())
                return ModbusResult<byte[]>.Fail("Not connected.");

            if (!ModbusTools.CheckTx(tx))
                return ModbusResult<byte[]>.Fail("Invalid Tx.");

            try
            {
                await requestLock.WaitAsync(cancellationToken);

                var sendResult = await SendAsync(tx, cancellationToken);
                if (!sendResult)
                    return ModbusResult<byte[]>.Fail("Send error.");

                return await ReadAsync(tx, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Request socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Request timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Request socket has been occured an error : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Request error.");
            }
            finally
            {
                requestLock.Release();
            }
        }

        private async ValueTask<bool> SendAsync(ModbusTx tx, CancellationToken cancellationToken = default)
        {
            try
            {
                var frame = ModbusTools.BuildTxFrame(tx);

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
                logger?.Warning("Send socket has been timeout : {ex.Message}", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.Error("Send socket has been occured an error : {ex.Message}", ex.Message);
                return false;
            }
        }

        private async ValueTask<ModbusResult<byte[]>> ReadAsync(ModbusTx tx, CancellationToken cancellationToken = default)
        {
            try
            {
                using var receiveTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveTimeoutToken.CancelAfter(Config.ReadTimeOut);
                receiveTimeoutToken.Token.ThrowIfCancellationRequested();

                // Read MBAP Header
                ReadOnlySequence<byte> mbap = await ReadExactAsync(ModbusParams.MBAP_LENGTH, receiveTimeoutToken.Token);

                // Calculate PDU length and read PDU
                var lowByte = mbap.FirstSpan[5];
                var highByte = mbap.FirstSpan[4];
                ushort pduLength = (ushort) (BitExtentions.ToUshort(lowByte, highByte) - 1);
                ReadOnlySequence<byte> rest = await ReadExactAsync(pduLength, receiveTimeoutToken.Token);

                // Rent memory
                ushort totalLength = (ushort) (ModbusParams.MBAP_LENGTH + pduLength);
                using var owner = MemoryPool<byte>.Shared.Rent(totalLength);
                var target = owner.Memory[..totalLength];

                // Merge MBAP and PDU
                mbap.CopyTo(target.Span);
                rest.CopyTo(target.Span[ModbusParams.MBAP_LENGTH..]);
                var parsed = ModbusRxParser.ParseRx(target, tx);

                if (parsed.IsSuccess)
                    return ModbusResult<byte[]>.Success(parsed.Data.Span.ToArray());
                return ModbusResult<byte[]>.Fail(parsed?.ErrorMessage ?? "Parse Rx error.");
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Receive socket has been timeout : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Receive timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Receive socket has been occured an error : {ex.Message}", ex.Message);
                return ModbusResult<byte[]>.Fail("Receive error.");
            }
        }

        private async ValueTask<ReadOnlySequence<byte>> ReadExactAsync(int length, CancellationToken cancellationToken = default)
        {
            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync(cancellationToken);
                    ReadOnlySequence<byte> sequence = result.Buffer;

                    if (sequence.Length >= length)
                    {
                        ReadOnlySequence<byte> slice = sequence.Slice(0, length);
                        reader.AdvanceTo(slice.End, sequence.End);
                        return slice;
                    }

                    if (result.IsCompleted) // 对端关闭连接但数据不足
                    {
                        reader.AdvanceTo(sequence.Start, sequence.End);
                        throw new EndOfStreamException(
                                $"Connection closed after {sequence.Length} bytes, expected {length}.");
                    }

                    reader.AdvanceTo(sequence.Start, sequence.End); // 数据不足，告知 Pipe 已检查到 buffer.End，等待更多数据    
                }
            }
            catch (OperationCanceledException)
            {
                logger?.Warning("The connection has been closed.");
                throw;
            }
            catch (Exception ex)
            {
                logger?.Error("ReadExact has been occured an error : {ex.Message}", ex.Message);
                throw;
            }
        }

        public bool CheckConnection() => IsConnected;

        public void Dispose()
        {
            socket?.Dispose();
            requestLock?.Dispose();
            logger?.Information("ModBusTCPMaster has been disposed.");
        }
    
    }
}