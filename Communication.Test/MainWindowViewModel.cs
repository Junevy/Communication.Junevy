using Communication.ModBus.Core;
using Communication.ModBus.Common;
using Communication.ModBus.ModbusRTU;
using Communication.ModBus.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using Communication.ModBus.ModbusTCP;
using Communication.ModBus.Factory;

namespace Communication.Test
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ModBusRTU mr;
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

        public ModBusRTUConfig Config { get; set; } = new();
        public Tx Tx { get; set; } = new();

        public MainWindowViewModel()
        {
            Logger log = new();
            Serilogger.SetInstance(log);

            this.mr = new ModBusRTU(Config);

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
            // if (Config.PortName is null)
            // {
            //     MessageBox.Show("The port name can not be null!");
            //     return;
            // }

            // mr.Connect();
            ModbusFactory factory = new();
            this.tcp = factory.Create(new ModBusTCPConfig());
            _ = tcp.Connect();
        }

        [RelayCommand]
        public async Task ExecuteAsync()
        {
            
            Tx tx = new Tx();

            // byte[] test = [0x01];
            // ushort test = 
            tx.Data = [1, 2, 132];

            var result = await tcp.RequestAsync(tx);

            Console.Write(result);
            // if (!ProcessTxData(out byte[] txData))
            //     return;

            // var currentAdrs = Tx.Start; // 记录当前地址
            // var currentLength = Tx.Length;

            // Tx.Data = txData;
            // CancellationTokenSource tk = new();

            // Console.WriteLine((ushort)Tx.FunctionCode);

            // var r = await mr.SendAsync(Tx, tk.Token);

            // if (r.IsSuccess && r.Data != null && r.Data[1] < (byte)ModBusFunctionCode.WriteCoils)
            // {
            //     // var coilsData = ModBusTools.ParseCoils(r.Data, currentLength);

            //     // for (int i = 0; i < currentLength; i++)
            //     // {
            //     //     DataList.Add(new ModBusData()
            //     //     {
            //     //         Address = (ushort)(currentAdrs + i),
            //     //         Value = coilsData[i]
            //     //     });
            //     // }

            //     var registerData = ModBusTools.ParseRegisters(r.Data, currentLength);

            //     for (int i = 0; i < currentLength; i++)
            //     {
            //         DataList.Add(new ModBusData()
            //         {
            //             Address = (ushort)(currentAdrs + i),
            //             Value = registerData[i]
            //         });
            //     }
            // }
            // else
            // {
            //     //var hexStr = r.Data?.ToHexString();
            //     //MessageBox.Show(hexStr);
            // }
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
            var temp = DataList.Select(x => x.Value).ToArray();
            txData = temp.ToByteArrayBigEndian();


            // 功能区分，处理写入数据和读取数据
            if (Tx.FunctionCode >= ModbusFunctionCode.WriteCoil)
            {
                if (txData == null || txData.Length <= 0)
                {
                    MessageBox.Show("The data can not be null!");
                    return false;
                }

                if (Tx.FunctionCode == ModbusFunctionCode.WriteCoil)
                    txData = txData.ToCoils();
                if (Tx.FunctionCode == ModbusFunctionCode.WriteMultiCoils)
                    txData = temp.ToMultiCoils();
            }
            // 非写入功能，清空数据列表
            else
            {
                DataList.Clear();
            }

            return true;
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
