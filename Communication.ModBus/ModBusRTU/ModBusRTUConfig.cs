using System.IO.Ports;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRTUConfig
    {
        public string PortName { get; set; }

        public int BaudRate { get; set; } = 9600;

        public Parity Parity { get; set; } = Parity.None;

        public int DataBits { get; set; } = 8;

        public StopBits StopBits { get; set; } = StopBits.One;

        public bool DtrEnable { get; set; } = false;

        public bool RtsEnable { get; set;} = false;

        public int WriteTimeOut { get; set; } = 1000;

        public int ReadTimeOut { get; set; } = 1000;

        public int IntervalTime { get; set; } = 20;

        public int RetryCount { get; set; } = 3;
    }
}
