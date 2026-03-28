namespace Communication.ModBus.Common
{
    /// <summary>
    /// ModBus 发送数据类，用于封装 ModBus 发送数据。
    /// </summary>
    public class Tx
    {
        /// <summary>
        /// 从站ID。
        /// </summary>
        public ushort SlaveId { get; set; } = 1;

        /// <summary>
        /// 功能码。
        /// </summary>
        public ushort FunctionCode { get; set; } = 0x01;

        /// <summary>
        /// 起始地址。
        /// </summary>
        public ushort Start { get; set; } = 0x00;

        /// <summary>
        /// 数据长度。
        /// </summary>
        public ushort Length { get; set; } = 0x01;

        /// <summary>
        /// 数据。
        /// </summary>
        public byte[]? Data { get; set; }
    }
}
