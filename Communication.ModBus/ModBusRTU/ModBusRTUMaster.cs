using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using System;
using System.IO.Ports;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRTUMaster : IModBus
    {
        private List<byte> receiveBuffer = [];
        private readonly object bufferLock = new();
        private readonly SerialPort serialPort;

        public bool IsConnected => serialPort.IsOpen;
        public ModBusRTUConfig Config { get; set; }

        public ModBusRTUMaster(ModBusRTUConfig config)
        {
            this.serialPort = new();
            this.serialPort.DataReceived += SerialPort_OnDataReceived;
            this.Config = config;
        }

        private void SerialPort_OnDataReceived(object sender, SerialDataReceivedEventArgs e) => OnDataReceived();

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
                Console.WriteLine(ex.Message);
                return false;
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
        public Result<bool[]> ReadCoils(byte slaveID, ushort start, ushort length)
        {
            if (!IsConnected) throw new InvalidOperationException("Serial port is not open.");

            var frame = BuildReadFrame(slaveID, 0x01, start, length);

            return Execute(frame, response =>
            {
                if (!TryParseReadCoils(response, length, out var data, out var err))
                    return Result<bool[]>.Fail(err);

                return Result<bool[]>.Success(data);
            });
        }

        private bool ReceiveFrame(out byte[] frame)
        {
            frame = null;
            DateTime start = DateTime.Now;

            while (true)
            {
                if (TryParseFrame(out frame))
                    return true;

                // 超时
                if ((DateTime.Now - start).TotalMilliseconds > Config.ReadTimeOut)
                    return false;

                Thread.Sleep(Config.IntervalTime);
            }
        }
        #endregion

        private bool TryParseFrame(out byte[] frame)
        {
            frame = null;

            lock (bufferLock)
            {
                if (receiveBuffer.Count < 5)
                    return false;

                for (int i = 0; i < receiveBuffer.Count - 1; i++)
                {
                    byte functionCode = receiveBuffer[i + 1];
                    int expectedLength = 0;

                    if ((functionCode & 0x80) != 0)
                        expectedLength = 5;
                    else if (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04)
                    {
                        int byteCount = receiveBuffer[i+2];
                        expectedLength = 3 + byteCount + 2;
                    }
                    else
                    {
                        receiveBuffer.RemoveAt(0);
                        return false;
                    }

                    if (i + expectedLength > receiveBuffer.Count)
                        return false;

                    frame = receiveBuffer.Skip(i).Take(expectedLength).ToArray();

                    if (!ValidateCRC(frame))
                    {
                        receiveBuffer.RemoveAt(0);
                        return false;
                    }

                    receiveBuffer.RemoveRange(0, i + expectedLength);
                    return true;
                }
            }
            return false;
        }

        private bool ValidateCRC(byte[] frame)
        {
            var dataWithoutCRC = frame.Take(frame.Length - 2).ToArray();
            var receivedCRC = frame.Skip(frame.Length - 2).ToArray();
            var calculatedCRC = CRC16.CRCLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }

        private bool TryParseReadCoils(byte[] response, ushort expectedLength, out bool[] result, out string err)
        {
            result = null;
            err = null;

            if ((response[0] & 0x80) != 0)
            {
                err = $"ModBus Exception: {response[2]}";
                return false;
            }

            List<bool> list = [];
            int byteCount = response[2];
            for (int i = 0; i < byteCount; i++)
            {
                var b = response[3 + i];

                // 每个字节包含8个线圈状态，依次解析
                for (int j = 0; j < 8; j++)
                    list.Add((b & (1 << j)) != 0);
            }

            result = list.Take(expectedLength).ToArray();
            return true;
        }

        private Result<T> Execute<T>(byte[] request, Func<byte[], Result<T>> parser)
        {
            for (int i = 0; i < Config.RetryCount; i++)
            {
                try
                {
                    this.serialPort.Write(request, 0, request.Length);

                    // 重试
                    if (!ReceiveFrame(out var response))
                        continue;

                    // 解析响应数据
                    return parser(response);
                }
                catch (Exception ex)
                {
                    return Result<T>.Fail(ex.Message);
                }
            }
            return Result<T>.Fail("Failed after retries.");
        }

        private byte[] BuildReadFrame(byte slaveID, byte functionCode, ushort start, ushort length)
        {
            List<byte> frame =
            [
                slaveID,
                functionCode,
                .. UshortHelper.ToBytesByBigEndian(start),
                .. UshortHelper.ToBytesByBigEndian(length),
            ];

            AddCRC16(frame);
            return frame.ToArray();
        }

        private void AddCRC16(List<byte> frame)
            => frame.AddRange(CRC16.CRCLittleEndian(frame.ToArray()));

        private void OnDataReceived()
        {
            byte[] temp = new byte[256];

            while (serialPort.BytesToRead > 0)
            {
                int len = serialPort.Read(temp, 0, temp.Length);

                lock (bufferLock)
                {
                    receiveBuffer.AddRange(temp.Take(len));
                }
            }
        }
    }
}
