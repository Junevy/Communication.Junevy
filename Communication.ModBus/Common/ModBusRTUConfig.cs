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

        public TimeSpan WriteTimeOut { get; set; } = TimeSpan.FromMilliseconds(1000);

        public TimeSpan ReadTimeOut { get; set; } = TimeSpan.FromMilliseconds(1000);
    }
}
