using Communication.ModBus.Core;

namespace Communication.ModBus.Common
{
    /// <summary>
    /// ModBus 接口，用于定义 ModBus 操作。
    /// </summary>
    public interface IModBus : IDisposable
    {
        /// <summary>
        /// 当前对象的协议类型
        /// </summary>
        public ModbusProtocolType ProtocolType { get; }

        // public bool IsAutoReceive { get; set; }

        public bool IsConnected { get; }

        /// <summary>
        /// 连接 ModBus 从站。
        /// </summary>
        /// <returns>是否成功连接。</returns>
        public bool Connect();

        /// <summary>
        /// 断开 ModBus 从站连接。
        /// </summary>
        public void Disconnect();

        public Rx<byte[]> Request(Tx tx);

        public Task<Rx<byte[]>> RequestAsync(Tx tx, CancellationToken cancellationToken = default);
    }
}
