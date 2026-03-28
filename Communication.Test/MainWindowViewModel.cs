using Communication.ModBus.Common;
using Communication.ModBus.ModBusRTU;
using Communication.ModBus.Utils;
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
        public Array RegionList => Enum.GetValues(typeof(ModBusFunctionCode));
        public int[] Bits { get; private set; } = [5, 6, 7, 8];
        public int[] BaudRates { get; private set; } = [9600, 19200, 38400, 57600, 115200];
        #endregion

        public ModBusRTUConfig Config { get; set; } = new();
        public MbCmd Cmd { get; set; } = new();
        public Tx Tx { get; set; } = new();

        public MainWindowViewModel()
        {
            this.mr = new ModBusRTUMaster(log, Config);

            Tx.OnFunctionCodeChanged += (f) =>
            {
                if (f >= ModBusFunctionCode.WriteCoils)
                    DataList.Clear();
            };

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
        public async Task ExecuteAsync()
        {
            if (!ProcessTxData(out byte[] txData))
                return;

            var currentAdrs = Tx.Start; // 记录当前地址
            CancellationTokenSource tk = new();

            Console.WriteLine((ushort)Tx.FunctionCode);

            var r = await mr.Build_Execute_TxAsync((byte)Tx.SlaveId, (ushort)Tx.FunctionCode, Tx.Start, Tx.Length, txData, tk.Token);

            if (r.IsSuccess && r.Data != null && r.Data[01] < (byte)ModBusFunctionCode.WriteCoils)
            {
                foreach (var b in r.Data)
                {
                    DataList.Add(new ModBusData() { Address = currentAdrs, Value = b });
                    currentAdrs++;
                }
            }
            else
            {
                var hexStr = r.Data?.ToHexString();
                MessageBox.Show(hexStr);
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

        private bool ProcessTxData(out byte[] txData)
        {
            txData = DataList.Select(x => x.Value).ToArray().ToByteArrayBigEndian();
            // txData = [];

            // 功能区分，处理写入数据和读取数据
            if (Tx.FunctionCode >= ModBusFunctionCode.WriteCoils)
            {
                if (txData == null || txData.Length <= 0)
                {
                    MessageBox.Show("The data can not be null!");
                    return false;
                }

                if (Tx.FunctionCode == ModBusFunctionCode.WriteCoils || Tx.FunctionCode == ModBusFunctionCode.WriteMultiCoils)
                    txData = txData.SetBitToFF();
            }
            // 非写入功能，清空数据列表
            else
                DataList.Clear();

            return true;
        }
    }
}
