namespace Communication.Modbus.Core
{
    public static class ModbusParams
    {
        public const int MBAP_LENGTH = 7;
        public const int TCP_PORT = 502;
        public const string TCP_LOCAL_ADDRESS = "127.0.0.1";
        public const int TCP_RESPONSE_MINIMUM_LENGTH = 8;
        public const ushort TCP_PROTOCOL_ID = 0x0000;
        public const ushort TCP_MAXPDU = 253;


        public const int RTU_BYTECOUNT_START = 2;
        public const int RTU_RESPONSE_MINIMUM_LENGTH = 5;



        public static readonly byte[] COIL_ON = [0xFF, 0x00];
        public static readonly byte[] COIL_OFF = [0x00, 0x00];
        public const byte EXCEPTION_FUNCCODE = 0x80;


        public const int CONNECT_TIMEOUT = 2000;
        public const int READ_TIMEOUT = 2000;
        public const int WRITE_TIMEOUT = 2000;
        public const int RETRY_COUNT = 3;
        public const int INTERVAL_TIME = 25;
        public const int MEMORY_POOL_SIZE = 1024;
    }

}
