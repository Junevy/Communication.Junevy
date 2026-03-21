namespace Communication.ModBus.Common
{
    interface IModBus
    {
        //public ModBusRTUConfig Config { get; set; }
        public bool Connect(ModBusRTUConfig config);
        public void Disconnect();
    }
}
