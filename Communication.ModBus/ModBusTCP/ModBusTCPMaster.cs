using System.Net.Sockets;
using Communication.ModBus.Common;
using Communication.ModBus.Core;
using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusTCP
{
    public sealed class ModBusTCPMaster : IModBus
    {
        private Socket socket;
        public ModBusTCPConfig Config { get; private set; } 
        public bool IsConnected => socket.Connected;

        public ModBusTCPMaster(ModBusTCPConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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
            throw new NotImplementedException();
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