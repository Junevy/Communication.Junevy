using System.Net.Sockets;
using Communication.ModBus.Common;
using Communication.ModBus.Core;
using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusTCP
{
    public sealed class ModBusTCPMaster : IModBus
    {
        private readonly Socket socket;
        private const int MbapHeaderLength = 6;
        private readonly ISerilog? logger = Serilogger.Instance;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        public ModBusTCPConfig Config { get; private set; }
        public bool IsConnected => socket.Connected;
        public ModbusProtocolType ProtocolType => ModbusProtocolType.TCP;

        public ModBusTCPMaster(ModBusTCPConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            this.Config = config;
        }

        public bool Connect()
        {
            if (!ModBusTools.ValidateAddress(Config.Address) || !ModBusTools.ValidatePort(Config.Port))
                return false;

            if (CheckConnection()) Disconnect();

            try
            {
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
            if (!ModBusTools.ValidateAddress(Config.Address) || !ModBusTools.ValidatePort(Config.Port))
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

        public Rx<byte[]> Request(Tx tx) 
        {
            if (!CheckConnection())
                return Rx<byte[]>.Fail("Not connected.");

            if (!ModBusTools.CheckTx(tx))
                return Rx<byte[]>.Fail("Invalid Tx.");

            requestLock.Wait();

            try
            {
                var sendResult = Send(tx);
                if (!sendResult.IsSuccess)
                    return sendResult;

                return Read(tx);
            }
            catch (Exception ex)
            {
                logger?.Error("Request socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Request error.");
            }
            finally
            {
                requestLock.Release();
            }
        }

        private Rx<byte[]> Send(Tx tx)
        {
            try
            {
                var frame = ModBusTools.BuildTxFrame(tx);
                
                socket.SendTimeout = Config.WriteTimeOut;
                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    int sent = socket.Send(frame, totalSent, frame.Length - totalSent, SocketFlags.None);
                    if (sent == 0)
                        return Rx<byte[]>.Fail("Connection closed during send.");
                    totalSent += sent;
                }
                return Rx<byte[]>.Success(frame);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger?.Warning("Send socket has been timeout : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Send timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Send socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Send error.");
            }
        }

        private Rx<byte[]> Read(Tx tx)
        {
            try
            {
                socket.ReceiveTimeout = Config.ReadTimeOut;

                // Read MBAP Header
                var mbapHeaderArray = new byte[MbapHeaderLength];
                var mbapHeader = ReadExact(mbapHeaderArray, MbapHeaderLength);

                ushort remainder = (ushort)(mbapHeader[4] << 8 | mbapHeader[5]);
                
                // Read PDU
                var pduFrameArray = new byte[remainder];
                var pdu = ReadExact(pduFrameArray, remainder);

                var fullFrame = mbapHeader.Concat(pdu).ToArray();

                return ModBusRxParser.ParseRx(fullFrame, tx);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
            {
                logger?.Warning("Receive socket has been timeout : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Receive timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Receive socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Receive error.");
            }
        }

        private byte[] ReadExact(byte[] buffer, int length)
        {
            int received = 0;
            while (received < length)
            {
                int readBytes = socket.Receive(buffer, received, length - received, SocketFlags.None);
                if (readBytes == 0)
                    throw new SocketException((int)SocketError.ConnectionReset);
                received += readBytes;
            }
            return buffer;
        }

        public async Task<Rx<byte[]>> RequestAsync(Tx tx, CancellationToken cancellationToken = default)
        {
            if (!CheckConnection())
                return Rx<byte[]>.Fail("Not connected.");

            if (!ModBusTools.CheckTx(tx))
                return Rx<byte[]>.Fail("Invalid Tx.");

            await requestLock.WaitAsync(cancellationToken);

            try
            {
                var sendResult = await SendAsync(tx, cancellationToken);
                if (!sendResult.IsSuccess)
                    return sendResult;

                return await ReadAsync(tx, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Request socket has been timeout : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Request timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Request socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Request error.");
            }
            finally
            {
                requestLock.Release();
            }
        }   

        private async Task<Rx<byte[]>> SendAsync(Tx tx, CancellationToken cancellationToken = default)
        {
            try
            {
                var frame = ModBusTools.BuildTxFrame(tx);

                using var sendTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                sendTimeoutToken.CancelAfter(Config.WriteTimeOut);
                sendTimeoutToken.Token.ThrowIfCancellationRequested();

                // 确保所有字节发送完成
                int totalSent = 0;
                while (totalSent < frame.Length)
                {
                    var sent = await socket.SendAsync(frame.AsMemory(totalSent), sendTimeoutToken.Token);

                    if (sent == 0)
                        return Rx<byte[]>.Fail("Connection closed during send.");

                    totalSent += sent;
                }
                return Rx<byte[]>.Success(frame);
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Send socket has been timeout : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Send timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Send socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Send error.");
            }
        }

        private async Task<Rx<byte[]>> ReadAsync(Tx tx, CancellationToken cancellationToken = default)
        {
            try
            {
                using var receiveTimeoutToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveTimeoutToken.CancelAfter(Config.ReadTimeOut);
                receiveTimeoutToken.Token.ThrowIfCancellationRequested();

                // Read MBAP Header
                var MbapHeaderArray = new byte[MbapHeaderLength];
                var mbapHeaer = await ReadExactAsync(MbapHeaderArray, MbapHeaderLength, receiveTimeoutToken.Token);

                ushort remiander = (ushort)(mbapHeaer[4] << 8 | mbapHeaer[5]);
                
                // Read PDU
                var pduFrameArray = new byte[remiander];
                var pdu = await ReadExactAsync(pduFrameArray, remiander, receiveTimeoutToken.Token);

                var fullFrame = mbapHeaer.Concat(pdu).ToArray();

                return ModBusRxParser.ParseRx(fullFrame, tx);
            }
            catch (OperationCanceledException ex)
            {
                logger?.Warning("Receive socket has been timeout : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Receive timeout.");
            }
            catch (Exception ex)
            {
                logger?.Error("Receive socket has been occured an error : {ex.Message}", ex.Message);
                return Rx<byte[]>.Fail("Receive error.");
            }
        }

        private async Task<byte[]> ReadExactAsync(byte[] buffer, int length, CancellationToken cancellationToken = default)
        {
            int received = 0;
            while (received < length)
            {
                var readBytes = await socket.ReceiveAsync(buffer.AsMemory(received), cancellationToken);
                received += readBytes;
            }
            return buffer;
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