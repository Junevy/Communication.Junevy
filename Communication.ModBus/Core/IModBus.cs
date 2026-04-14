using Communication.ModBus.Core;

namespace Communication.ModBus.Common
{
    /// <summary>
    /// ModBus 接口，用于定义 ModBus 操作。
    /// </summary>
    interface IModBus : IDisposable
    {
        public bool IsConnected { get; }

        //public ModBusRTUConfig Config { get; set; }

        /// <summary>
        /// 连接 ModBus 从站。
        /// </summary>
        /// <returns>是否成功连接。</returns>
        public bool Connect();

        /// <summary>
        /// 断开 ModBus 从站连接。
        /// </summary>
        public void Disconnect();

        /// <summary>
        /// 初始化 ModBus 连接。
        /// </summary>
        /// <exception cref="Exception">当配置参数无效时，抛出异常。</exception>
        // public void InitialConnection();

        /// <summary>
        /// 发送 ModBus 报文。
        /// </summary>
        /// <param name="tx">ModBus 请求报文。</param>
        /// <returns>ModBus 响应报文。</returns>
        public Rx<byte[]> Send(Tx tx);

        /// <summary>
        /// 异步发送 ModBus 报文。
        /// </summary>
        /// <param name="tx">ModBus 请求报文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>ModBus 响应报文。</returns>
        public Task<Rx<byte[]>> SendAsync(Tx tx, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 接收 ModBus 响应报文。
        /// </summary>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <returns>ModBus 响应报文。</returns>
        public Rx<byte[]> Receive(byte slaveID, byte functionCode);

        /// <summary>
        /// 异步接收 ModBus 响应报文。
        /// </summary>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>ModBus 响应报文。</returns>
        public Task<Rx<byte[]>> ReceiveAsync(byte slaveID, byte functionCode, CancellationToken cancellationToken = default);
    }
}
