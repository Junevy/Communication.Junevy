using System.Buffers;

namespace Communication.Modbus.Core
{
    /// <summary>
    /// ModBus 接口，用于定义 ModBus 操作。
    /// </summary>
    public interface IModbus : IDisposable
    {
        /// <summary>
        /// 当前对象的协议类型
        /// </summary>
        public ModbusProtocolType ProtocolType { get; }

        /// <summary>
        /// 是否已连接到 ModBus 从站
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// 连接 ModBus 从站。
        /// </summary>
        /// <returns>是否成功连接。</returns>
        public bool Connect();

        /// <summary>
        /// 异步连接 ModBus 从站。
        /// </summary>
        /// <returns>是否成功连接。</returns>
        public Task<bool> ConnectAsync();

        /// <summary>
        /// 断开 ModBus 从站连接。
        /// </summary>
        public void Disconnect();

        /// <summary>
        /// 发送 ModBus 指令
        /// </summary>
        /// <param name="tx">ModBus 指令</param>
        /// <returns>ModBus 指令的响应</returns>
        public ModbusResult<ReadOnlyMemory<byte>> Request(ModbusTx tx);

        /// <summary>
        /// 异步发送 ModBus 指令
        /// </summary>
        /// <param name="tx">ModBus 指令</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>ModBus 指令的响应</returns>
        public Task<ModbusResult<ReadOnlyMemory<byte>>> RequestAsync(ModbusTx tx, CancellationToken cancellationToken = default);
    }
}
