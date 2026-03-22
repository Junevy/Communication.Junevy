using Communication.ModBus.ModBusRTU;

namespace Communication.ModBus.Common
{
    interface IModBus : IDisposable
    {
        //public ModBusRTUConfig Config { get; set; }
        public bool Connect();
        public void Disconnect();
    }
}
