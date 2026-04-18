using Communication.Modbus.Core;
using System.IO.Ports;

namespace Communication.Modbus.RTU
{
    public class ModbusRTUConfig
    {
        /// <summary>
        /// 串口名称。
        /// </summary>
        public string PortName { get; set; } = "COM1" ?? throw new ArgumentNullException(nameof(PortName));
        
        /// <summary>
        /// 波特率。
        /// </summary>
        public int BaudRate { get; set; } = 9600;
        
        /// <summary>
        /// 校验位。
        /// </summary>
        public Parity Parity { get; set; } = Parity.None;
        
        /// <summary>
        /// 数据位。
        /// </summary>
        public int DataBits { get; set; } = 8;
        
        /// <summary>
        /// 停止位。
        /// </summary>
        public StopBits StopBits { get; set; } = StopBits.One;
        
        /// <summary>
        /// 是否启用 DTR。
        /// </summary>
        public bool DtrEnable { get; set; } = false;
        
        /// <summary>
        /// 是否启用 RTS。
        /// </summary>
        public bool RtsEnable { get; set;} = false;

        /// <summary>
        /// 写超时时间。
        /// </summary>
        public int WriteTimeOut { get; set; } = ModbusParams.WRITE_TIMEOUT;

        /// <summary>
        /// 读超时时间。
        /// </summary>
        public int ReadTimeOut { get; set; } = ModbusParams.READ_TIMEOUT;

        /// <summary>
        /// 重试次数。
        /// </summary>
        public int RetryCount { get; set; } = ModbusParams.RETRY_COUNT;

        /// <summary>
        /// 等待报文Rx间隔时间。
        /// </summary>
        public int IntervalTime { get; set; } = ModbusParams.INTERVAL_TIME;

    }
}
