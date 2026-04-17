using Communication.ModBus.Core;
using Communication.ModBus.ModBusRTU;
using Communication.ModBus.ModBusTCP;

namespace Communication.ModBus.Factory
{
    /// <summary>
    /// ModBus 工厂接口，用于创建 ModBus 实例。
    /// </summary>
    interface IModBusFactory
    {
        /// <summary>
        /// 创建 ModBus 实例。
        /// </summary>
        /// <param name="config">配置。</param>
        /// <returns>ModBus 实例。</returns>
        IModBus Create(ModBusTCPConfig config);
        IModBus Create(ModBusRTUConfig config);
    }
}