namespace Communication.Modbus.Core
{
    public static class ModbusParams
    {
        public const int MBAP_LENGTH = 7;

        public const int TCP_PORT = 502;

        public const string LOCAL_ADDRESS = "127.0.0.1";

        public const int TCP_DATA_START = 6;

        public const int CONNECT_TIMEOUT = 2000;

        public const int READ_TIMEOUT = 2000;

        public const int WRITE_TIMEOUT = 2000;

        public const int RETRY_COUNT = 3;

        public const int INTERVAL_TIME = 25;
    }
    
}
