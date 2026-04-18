using Communication.Modbus.Core;
using Communication.Modbus.RTU;
using Communication.Modbus.TCP;

namespace Communication.Modbus.Factory
{
    /// <summary>
    /// ModBus 工厂，用于创建 ModBus 实例。
    /// </summary>
    public sealed class ModbusFactory : IModbusFactory
    {
        public IModbus Create(ModbusTCPConfig config)
        {
            ModbusTCP socket = new(config);
            return socket;
        }

        public IModbus Create(ModbusRTUConfig config)
        {
            ModbusRTU serialPort = new(config);
            return serialPort;
        }
    }
}
