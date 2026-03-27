using Communication.ModBus.Common;
using Communication.ModBus.ModBusRTU;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;

namespace Communication.Test
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ModBusRTUMaster mr;
        private readonly ISerilog log;
        private bool isConnected = false;
        public bool IsConnected
        {
            get => isConnected;
            set => SetProperty(ref isConnected, value);
        }

        #region observable collection
        public ObservableCollection<ModBusData> DataList { get; set; } = [];
        public ObservableCollection<string> Serials { get; private set; } = new(SerialPort.GetPortNames());

        public Array ParityList => Enum.GetValues(typeof(Parity));
        public Array StopBitsList => Enum.GetValues(typeof(StopBits));
        public Array RegionList => Enum.GetValues(typeof(Regions));
        public int[] Bits { get; private set; } = [5, 6, 7, 8];
        public int[] BaudRates { get; private set; } = [9600, 19200, 38400, 57600, 115200];
        #endregion

        public ModBusRTUConfig Config { get; set; } = new();
        public MbCmd Cmd { get; set; } = new();
        public Tx Tx { get; set; } = new();

        [ObservableProperty]
        private Regions currentRegion = Regions.Coils_0x;

        public MainWindowViewModel()
        {
            this.mr = new ModBusRTUMaster(log, Config);
            StateMonitor();
        }

        [RelayCommand]
        public void Connect()
        {
            if (Config.PortName is null)
            {
                MessageBox.Show("The port name can not be null!");
                return;
            }

            mr.Connect();
        }

        [RelayCommand]
        public async Task ReadAsync()
        {
            DataList.Clear();
            var currentAdrs = Tx.Start;

            Tx.FunctionCode = CurrentRegion switch
            {
                Regions.Coils_0x => 0x01,
                Regions.DiscreteInputs_1x => 0x02,
                Regions.InputRegister_3x => 0x03,
                Regions.HodingRegister_4x => 0x04,
                _ => 0x01,
            };

            CancellationTokenSource tk = new();
            var r = await mr.Build_Execute_TxAsync((byte)Tx.SlaveId, Tx.FunctionCode, Tx.Start, Tx.Length, tk.Token);

            if (r.IsSuccess && r.Data != null)
            {
                foreach (var b in r.Data)
                {
                    DataList.Add(new ModBusData() { Address = (ushort)currentAdrs, Value = b });
                    currentAdrs++;
                }
            }
            else
            {
                DataList.Add(new ModBusData() { Address = 0x00, Value = 0x00});
            }
        }

        public async Task WriteAsync(){
            CancellationTokenSource tk = new();
            var r = await mr.WriteAsync((byte)Tx.SlaveId, Tx.FunctionCode, Tx.Start, Tx.Length, Tx.Data, tk.Token);
            if (r.IsSuccess)
            {
                MessageBox.Show("Write success!");
            }
            else
            {
                MessageBox.Show($"Write failed! Exception code : {r.ExceptionCode}");
            }
        }

        [RelayCommand]
        public void Disconnect()
        {
            mr.Disconnect();
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

    }
}
