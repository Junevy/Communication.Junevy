using Communication.Modbus.Core;
using Communication.Modbus.Common;
using Communication.Modbus.RTU;
using Communication.Modbus.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using Communication.Modbus.TCP;
using Communication.Modbus.Factory;
using Communication.Modbus.Extensions;

namespace Communication.Test
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ModbusRTU mr;
        private IModbus tcp;
        
        private bool isConnected = false;
        public bool IsConnected
        {
            get => isConnected;
            set => SetProperty(ref isConnected, value);
        }

        [ObservableProperty]
        private ushort length = 1;

        #region observable collection
        public ObservableCollection<ModBusData> DataList { get; set; } = [];
        public ObservableCollection<string> Serials { get; private set; } = new(SerialPort.GetPortNames());

        public Array ParityList => Enum.GetValues(typeof(Parity));
        public Array StopBitsList => Enum.GetValues(typeof(StopBits));
        public Array RegionList => Enum.GetValues(typeof(ModbusFunctionCode));
        public int[] Bits { get; private set; } = [5, 6, 7, 8];
        public int[] BaudRates { get; private set; } = [9600, 19200, 38400, 57600, 115200];
        #endregion

        public ModbusRTUConfig Config { get; set; } = new();
        public ModbusTx Tx { get; set; } = new();

        public MainWindowViewModel()
        {
            Logger log = new();
            Serilogger.SetInstance(log);

            this.mr = new ModbusRTU(Config);

            // 监听功能码变化, 对应DataGrid的变化
            Tx.OnFunctionCodeChanged += (f) =>
            {
                if (f >= ModbusFunctionCode.WriteCoil)
                {
                    if (DataList.Count < Length)
                    {
                        var l = DataList.Count;
                        for (int i = 0; i < Length - l; i++)
                        {
                            DataList.Add(new ModBusData());
                        }
                    }

                    if (DataList.Count > Length)
                    {
                        var l = DataList.Count;

                        for (int i = 0; i < l - Length; i++)
                        {
                            DataList.RemoveAt(DataList.Count - 1);
                        }
                    }

                }
            };

            StateMonitor();
        }

        [RelayCommand]
        public void Connect()
        {
            ModbusFactory factory = new();
            var result = factory.TryAddModbus(out tcp, new ModbusTCPConfig(), "test");
            if (result)
                _ = tcp?.Connect();
        }

        [RelayCommand]
        public async Task ExecuteAsync()
        {

            var r = await tcp.ReadCoilsAsync(1,0,4);
            Console.Write(r.ToString());
  
        }

        [RelayCommand]
        public void Disconnect()
        {
            tcp?.Disconnect();
        }

        private void StateMonitor()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    bool current = mr.IsConnected;

                    if (current != IsConnected)
                    {
                        IsConnected = current;
                    }

                    await Task.Delay(1000); // 轮询间隔
                }
            });
        }


        partial void OnLengthChanged(ushort oldValue, ushort newValue)
        {
            DataList.Clear();

            if (newValue > 128)
                newValue = 127;

            for (ushort i = 0; i < newValue; i++)
            {
                DataList.Add(new ModBusData() { Address = i });
            }

            Tx.Length = newValue;
        }

    }
}
