using Communication.Modbus.Core;
using Communication.Modbus.TCP;
using Communication.Modbus.RTU;

namespace Communication.Modbus.Factory
{
    /// <summary>
    /// ModBus 工厂接口，用于创建 ModBus 实例。
    /// </summary>
    public interface IModbusFactory
    {
        public bool TryGetMosbus(out IModbus? modbus, string key);

        public bool TryAddModbus(out IModbus? socket, ModbusTCPConfig config, string? key = null);

        public bool TryAddModbus(out IModbus? socket, ModbusRTUConfig config, string? key = null);
    }
}