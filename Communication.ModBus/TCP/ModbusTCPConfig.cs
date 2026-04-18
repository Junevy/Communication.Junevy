namespace Communication.Modbus.TCP
{
    public class ModbusTCPConfig
    {
        public string Address {get; set;} = "127.0.0.1";

        public int Port {get; private set; } = 502;

        public bool Reconnect {get; set;} = false;


        public bool AutoReceive {get; set;} = true;
        /// <summary>
        /// 连接超时时间。
        /// </summary>
        public int ConnectTimeout { get; set; } = 2000;

        /// <summary>
        /// 写超时时间。
        /// </summary>
        public int WriteTimeOut { get; set; } = 2000;
        
        /// <summary>
        /// 读超时时间。
        /// </summary>
        public int ReadTimeOut { get; set; } = 2000;
        
        /// <summary>
        /// 重试次数。
        /// </summary>
        public int RetryCount { get; set; } = 0;

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