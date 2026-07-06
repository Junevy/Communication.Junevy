namespace Junevy.Communication.Modbus.Core.Models
{
    public sealed class ModbusCommEventLog
    {
        public ushort Status { get; set; }

        public ushort EventCount { get; set; }

        public ushort MessageCount { get; set; }

        public byte[] Events { get; set; } = Array.Empty<byte>();
    }
}
