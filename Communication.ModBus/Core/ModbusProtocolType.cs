namespace Communication.ModBus.Core
{
    /// <summary>
    /// Modbus协议类型
    /// </summary>
    public enum ModbusProtocolType
    {
        /// <summary>
        /// Modbus TCP协议
        /// </summary>
        TCP = 0x0000,

        /// <summary>
        /// Modbus RTU协议
        /// </summary>
        RTU,
    }
}