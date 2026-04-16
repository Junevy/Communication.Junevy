namespace Communication.ModBus.Core
{
    /// <summary>
    /// ModBus 发送数据类，用于封装 ModBus 发送数据。
    /// </summary>
    public class Tx
    {

        /// <summary>
        /// 功能码改变事件。
        /// </summary>
        public event Action<ModBusFunctionCode>? OnFunctionCodeChanged;

        public ushort TransactionId { get; set; } = 0x0000;

        public ModbusProtocolType ProtocolType {get; set;} 

        /// <summary>
        /// 从站ID。
        /// </summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>
        /// 功能码。
        /// </summary>
        private ModBusFunctionCode functionCode = ModBusFunctionCode.ReadCoils;
        public ModBusFunctionCode FunctionCode 
        {
            get => functionCode;
            set {
                functionCode = value;
                InvokeOnFunctionCodeChanged();  // 调用事件
            }
        }

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
        public byte[]? Data { get; set; } = null;


        /// <summary>
        /// 功能码改变事件。
        /// </summary>
        public void InvokeOnFunctionCodeChanged()
        {
            OnFunctionCodeChanged?.Invoke(FunctionCode);
        }
    }
}
