using System.Net.Sockets;
using Communication.ModBus.Common;
using Communication.ModBus.Core;
using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusTCP
{
    public sealed class ModBusTCPMaster : IModBus
    {
        private Socket socket;
        private readonly ISerilog? logger = Serilogger.Instance;

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
            if (ModBusTools.ValidateAddress(Config.Address) || ModBusTools.ValidatePort(Config.Port))
                return false;

            socket.Connect(Config.Address, Config.Port);
            return true;
        }

        public void Disconnect()
        {
            try
            {
                socket.Disconnect(true);
                socket.Dispose();
            }
            catch (Exception ex)
            {
                logger?.Warning("Close socket has been occured an error : {ex.Message}", ex.Message);      
            }
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public Rx<byte[]> Receive(byte slaveID, byte functionCode)
        {
            throw new NotImplementedException();
        }

        public Task<Rx<byte[]>> ReceiveAsync(byte slaveID, byte functionCode, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Rx<byte[]> Send(Tx tx)
        {
            throw new NotImplementedException();
        }

        public Task<Rx<byte[]>> SendAsync(Tx tx, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}