using Communication.ModBus.Core;
using Communication.ModBus.ModBusRTU;
using Communication.ModBus.ModBusTCP;

namespace Communication.ModBus.Factory
{
    /// <summary>
    /// ModBus 工厂，用于创建 ModBus 实例。
    /// </summary>
    public class ModBusFactory : IModBusFactory
    {
        public IModBus Create(ModBusTCPConfig config)
        {
            ModBusTCPMaster socket = new(config);
            return socket;
        }

        public IModBus Create(ModBusRTUConfig config)
        {
            ModBusRTUMaster serialPort = new(config);
            return serialPort;
        }
    }
}
