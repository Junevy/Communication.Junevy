using Communication.ModBus.ModBusRTU;

﻿namespace Communication.ModBus.Common
{
    /// <summary>
    /// ModBus 接口，用于定义 ModBus 操作。
    /// </summary>
    interface IModBus : IDisposable
    {
        //public ModBusRTUConfig Config { get; set; }
        public bool Connect();
        public void Disconnect();
    }
}
