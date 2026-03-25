using Communication.ModBus.Common;
using Communication.ModBus.ModBusRTU;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO.Ports;

namespace Communication.Test
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ModBusRTUMaster mr;
        private readonly ISerilog log;


        public ModBusRTUConfig Config { get; set; } = new();

        public MbCmd Cmd { get; set; } = new();

        public ObservableCollection<string> Serials { get; private set; } = [];

        public MainWindowViewModel()
        {
            this.mr = new ModBusRTUMaster(log, Config);

            var portName = SerialPort.GetPortNames();
            foreach (var name in portName)
            {
                Serials.Add(name);
            }
        }

        [RelayCommand]
        public void Connect()
        {
            mr.Connect();
        }

        [RelayCommand]
        public async Task ReadCoilsAsync()
        {
            CancellationTokenSource tk = new();
            var r = await mr.ReadCoilsAsync((byte)Cmd.SlaveId, Cmd.Start, Cmd.Count, tk.Token);
            Console.WriteLine(r);
        }


    }
}
