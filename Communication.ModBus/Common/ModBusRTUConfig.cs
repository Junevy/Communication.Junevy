using System.IO.Ports;

namespace Communication.ModBus.Common
{
    public class ModBusRTUConfig
    {
        public string PortName { get; set; }
        
        public int BaudRate { get; set; }

        public Parity Parity { get; set; } = Parity.None;

        public int DataBits { get; set; } = 8;

        public StopBits StopBits { get; set; } = StopBits.One;

        public bool DtrEnable { get; set; } = false;

        public bool DtrDisable { get; set;} = false;

        public int WriteTimeOut { get; set; } = 1000;

        public int ReadTimeOut { get; set; } = 1000;

        public int IntervalTime { get; set; } = 10;

        public int RetryCount { get; set; } = 3;
    }
}
