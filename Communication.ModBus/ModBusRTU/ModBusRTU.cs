using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using System.IO.Ports;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRTU(ModBusRTUConfig config) : IModBus
    {
        private readonly SerialPort serialPort = new();

        public bool IsConnected => serialPort.IsOpen;
        public ModBusRTUConfig Config { get; set; } = config;

        public bool Connect(ModBusRTUConfig config)
        {
            if (serialPort.IsOpen)
                Disconnect();

            serialPort.PortName = config.PortName;
            serialPort.BaudRate = config.BaudRate;
            serialPort.Parity = config.Parity;
            serialPort.DataBits = config.DataBits;
            serialPort.StopBits = config.StopBits;
            serialPort.DtrEnable = config.DtrEnable;
            serialPort.RtsEnable = config.DtrDisable;

            try
            {
                serialPort.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message); return false;
            }
            return true;
        }

        public void Disconnect()
        {
            try
            {
                serialPort.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        #region Read Coils _ 01H
        public void ReadCoils(byte slaveID, ushort start, ushort length)
        {
            if (!IsConnected) return;

            List<byte> sendCommand =
            [
                slaveID,
                0x01,
                .. UshortHelper.ToBytesByBigEndian(start),
                .. UshortHelper.ToBytesByBigEndian(length),
            ];

            byte[] command = sendCommand.ToArray();
            byte[] crc = CRC16.CRCLittleEndian(command);
            sendCommand.AddRange(crc);



        }


        #endregion
    }
}
