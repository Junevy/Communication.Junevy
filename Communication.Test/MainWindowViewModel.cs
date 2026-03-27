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
        public ObservableCollection<ModBusData> DataList { get; set; } = new();
        public ObservableCollection<string> Serials { get; private set; } = [];
        public ObservableCollection<Regions> RegionList { get; private set; } = [];
        public ObservableCollection<Parity> Parities { get; private set; } = [];
        public ObservableCollection<StopBits> Stops { get; private set; } = [];
        public ObservableCollection<int> Bits { get; private set; } = [];
        public ObservableCollection<int> BaudRates { get; private set; } = [];
        #endregion

        public ModBusRTUConfig Config { get; set; } = new();
        public MbCmd Cmd { get; set; } = new();
        public Tx Tx { get; set; } = new();

        [ObservableProperty]
        private Regions currentRegion = Regions.Coils_0x;

        public MainWindowViewModel()
        {
            this.mr = new ModBusRTUMaster(log, Config);
            Initialize();
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
            var r = await mr.ReadAsync((byte)Tx.SlaveId, Tx.FunctionCode, Tx.Start, Tx.Length, tk.Token);

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

        private void Initialize()
        {
            var portName = SerialPort.GetPortNames();
            foreach (var name in portName)
            {
                Serials.Add(name);
            }

            StateMonitor();

            RegionList.Add(Regions.Coils_0x);
            RegionList.Add(Regions.DiscreteInputs_1x);
            RegionList.Add(Regions.InputRegister_3x);
            RegionList.Add(Regions.HodingRegister_4x);

            Parities.Add(Parity.Even);
            Parities.Add(Parity.Odd);
            Parities.Add(Parity.None);

            Stops.Add(StopBits.One);
            Stops.Add(StopBits.Two);
            Stops.Add(StopBits.None);

            Bits.Add(5);
            Bits.Add(6);
            Bits.Add(7);
            Bits.Add(8);

            BaudRates.Add(9600);
            for (int i = 1; i <= 12; i++)
            {
                if (i % 2 == 0)
                    BaudRates.Add(i * 9600);
                if (i == 6)
                    i = 11;
            }
        }

    }
}
