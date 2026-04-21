using Communication.Modbus.Core;

namespace Communication.Modbus.TCP
{
    public class ModbusTCPConfig
    {
        public string Address { get; set; } = ModbusParams.TCP_LOCAL_ADDRESS ?? throw new ArgumentNullException(nameof(Address));

        public int Port { get; private set; } = ModbusParams.TCP_PORT;

        public bool Reconnect {get; set;} = false;

        /// <summary>
        /// 连接超时时间。
        /// </summary>
        public int ConnectTimeout { get; set; } = ModbusParams.CONNECT_TIMEOUT;

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

        public bool SetPort(int port = 502)
        {
            if (port < 1024 || port > 65535)
            {
                return false;
            }

            this.Port = port;
            return true;
        }


    }
}