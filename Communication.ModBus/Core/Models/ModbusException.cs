namespace Communication.Modbus.Core.Models
{
    public class ModbusException : Exception
    {
        public ModbusErrorCode ErrorCode { get; }

        public ModbusException(ModbusErrorCode errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }

        public ModbusException(ModbusErrorCode errorCode, string message, Exception innerException) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    public enum ModbusErrorCode
    {
        InvalidAddress = 0x02,
        InvalidQuantity = 0x03,
        InvalidValue = 0x03,
        InvalidData = 0x03,
        ServerFailure = 0x04,
        Acknowledge = 0x05,
        ServerBusy = 0x06,
        GatewayUnavailable = 0x0A,
        GatewayPathUnavailable = 0x0B
    }
}
