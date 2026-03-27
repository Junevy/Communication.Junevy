namespace Communication.ModBus.Common
{
    public class Tx
    {
        public ushort SlaveId { get; set; } = 1;

        public ushort FunctionCode { get; set; } = 0x01;

        public ushort Start { get; set; } = 0x00;

        public ushort Length { get; set; } = 0x01;
    }
}
