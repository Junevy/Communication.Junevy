using Communication.ModBus.Core;
using Communication.ModBus.ModbusRTU;
using Communication.ModBus.ModbusTCP;

namespace Communication.ModBus.Factory
{
    /// <summary>
    /// ModBus 工厂，用于创建 ModBus 实例。
    /// </summary>
    public class ModbusFactory : IModbusFactory
    {
        public IModbus Create(ModBusTCPConfig config)
        {
            ModBusTCP socket = new(config);
            return socket;
        }

        public IModbus Create(ModBusRTUConfig config)
        {
            ModBusRTU serialPort = new(config);
            return serialPort;
        }
    }
}
