using Communication.Modbus.Core;
using Communication.Modbus.TCP;
using Communication.Modbus.RTU;

namespace Communication.Modbus.Factory
{
    /// <summary>
    /// ModBus 工厂接口，用于创建 ModBus 实例。
    /// </summary>
    interface IModbusFactory
    {
        /// <summary>
        /// 创建 ModBus 实例。
        /// </summary>
        /// <param name="config">配置。</param>
        /// <returns>ModBus 实例。</returns>
        IModbus Create(ModbusTCPConfig config);
        IModbus Create(ModbusRTUConfig config);
    }
}