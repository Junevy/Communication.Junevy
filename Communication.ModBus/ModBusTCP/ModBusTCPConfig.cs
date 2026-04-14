namespace Communication.ModBus.ModBusTCP
{
    public class ModBusTCPConfig
    {
        public string Address {get; set;} = "127.0.0.1";

        public int Port {get; private set; } = 502;


        public bool SetPort(int port = 502)
        {
            if (port < 1024 || port > 65535)
            {
                return false;
            }

            this.Port = port;
            return true;
        }
    }
}