namespace Communication.Test
{
    public class MbCmd
    {
        public ushort SlaveId { get; set; } = 1;

        public ushort FunctionCode { get; set; } = 0x01;

        public ushort Start { get; set; } = 0x00;

        public ushort Count { get; set; } = 0x01;
    }
}
