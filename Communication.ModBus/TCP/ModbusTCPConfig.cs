using Communication.Modbus.Core.Interfaces;

namespace Communication.Modbus.TCP
{
    public class ModbusTCPConfig : IModbusConfig
    {
        public string Address { get; set; } = "127.0.0.1";

        public int Port { get; private set; } = 502;

        public bool Reconnect { get; set; } = false;

        /// <summary>
        /// Delay between retry/reconnect attempts in milliseconds.
        /// </summary>
        public int RetryInterval { get; set; } = 100;

        /// <summary>
        /// Connection timeout in milliseconds.
        /// </summary>
        public int ConnectTimeout { get; set; } = 2000;

        /// <summary>
        /// Write timeout in milliseconds.
        /// </summary>
        public int WriteTimeOut { get; set; } = 2000;

        /// <summary>
        /// Read timeout in milliseconds.
        /// </summary>
        public int ReadTimeOut { get; set; } = 2000;

        /// <summary>
        /// Number of retry attempts.
        /// </summary>
        public int RetryCount { get; set; } = 3;

        public bool SetPort(int port = 502)
        {
            if ((port < 1024 || port > 65535) && port != 502)
            {
                return false;
            }

            this.Port = port;
            return true;
        }
    }
}
