namespace Communication.Modbus.Core.Models
{
    public sealed class ModbusCommEventCounter
    {
        public ushort Status { get; set; }

        public ushort EventCount { get; set; }
    }
}
