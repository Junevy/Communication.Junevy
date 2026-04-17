using Communication.ModBus.Core;
using Communication.ModBus.ModbusTCP;
using Communication.ModBus.ModbusRTU;

namespace Communication.ModBus.Factory
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
        IModbus Create(ModBusTCPConfig config);
        IModbus Create(ModBusRTUConfig config);
    }
}