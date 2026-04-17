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

            var result = Task.Run(ConnectAsync);
            return result.GetAwaiter().GetResult();
        }

        private async Task<bool> ConnectAsync()
        {
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
            var result = Task.Run(async () => await RequestAsync(tx));
            return result.GetAwaiter().GetResult();
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