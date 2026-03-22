using Communication.ModBus.Common;
using Communication.ModBus.Utils;
using System.Diagnostics;
using System.IO.Ports;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRTUMaster : IModBus
    {
        private readonly List<byte> receiveBuffer = [];
        private readonly object bufferLock = new();
        private readonly SerialPort serialPort;

        public bool IsConnected => serialPort.IsOpen;
        public ModBusRTUConfig Config { get; private set; }

        public ModBusRTUMaster(ModBusRTUConfig config)
        {
            this.serialPort = new();
            this.serialPort.DataReceived += SerialPort_OnDataReceived;
            this.Config = config;
        }

        private void SerialPort_OnDataReceived(object sender, SerialDataReceivedEventArgs e)
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
        public bool Connect()
        {
            if (serialPort.IsOpen)
                Disconnect();

            serialPort.PortName = Config.PortName;
            serialPort.BaudRate = Config.BaudRate;
            serialPort.Parity = Config.Parity;
            serialPort.DataBits = Config.DataBits;
            serialPort.StopBits = Config.StopBits;
            serialPort.DtrEnable = Config.DtrEnable;
            serialPort.RtsEnable = Config.RtsEnable;

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

            var frame = ModBusHelper.BuildReadFrame(slaveID, 0x01, start, length);

            return Execute(frame,response =>
            {
                if (!TryParseReadCoils(response,slaveID, 0x01, length, out var data, out var err))
                    return Result<bool[]>.Fail(err);
                
                return Result<bool[]>.Success(data);
            });
        }

        private bool ReceiveFrame(byte slaveID, byte funcCode, out byte[] frame)
        {
            //frame = null;
            //DateTime start = DateTime.Now;

            var sw = Stopwatch.StartNew();

            while (true)
            {
                if (TryParseFrame(slaveID, funcCode, out frame))
                    return true;
                
                // 超时
                if (sw.ElapsedMilliseconds > Config.ReadTimeOut)
                    return false;

                Thread.Sleep(Config.IntervalTime);
            }
        }
        #endregion

        /// <summary>
        /// 校验数据帧
        /// 数据帧是否完整、从站ID和功能码是否匹配、CRC16校验是否通过
        /// </summary>
        /// <param name="slaveID">主站请求的从站ID</param>
        /// <param name="funcCode">主站请求的功能码</param>
        /// <param name="frame">返回的符合条件的数据帧</param>
        /// <returns>数据帧是否符合要求</returns>
        private bool TryParseFrame(byte slaveID, byte funcCode, out byte[] frame)
        {
            frame = null;

            lock (bufferLock)
            {
                if (receiveBuffer.Count < 5)
                    return false;

                // 保证List 最短长度为5
                for (int i = 0; i <= receiveBuffer.Count - 5; i++)
                {
                    byte functionCode = receiveBuffer[i + 1];
                    byte ID = receiveBuffer[i];
                    int expectedLength = 0;

                    // 判断是否异常码
                    if ((functionCode & 0x80) != 0)
                    {
                        if (ID != slaveID || functionCode != (funcCode | 0x80))
                            continue;
                        expectedLength = 5;
                    }

                    // Read
                    else if (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04)
                    {
                        // 校验功能码和从站ID
                        if (functionCode != funcCode || ID != slaveID)
                            continue;

                        int byteCount = receiveBuffer[i + 2];
                        expectedLength = 3 + byteCount + 2;
                    }
                    //Write and Read
                    else if (functionCode == 0x05 || functionCode == 0x06 || functionCode == 0x0F || functionCode == 0x10)
                    {
                        // 校验功能码和从站ID
                        if (functionCode != funcCode || ID != slaveID)
                            continue;
                        // ...
                        expectedLength = 8;
                    }
                    else
                        continue;

                    // 判断是否满足完整帧的长度
                    if (i + expectedLength > receiveBuffer.Count)
                        return false;

                    frame = receiveBuffer.Skip(i).Take(expectedLength).ToArray();

                    if (ModBusHelper.ValidateCRC(frame))
                    {
                        receiveBuffer.RemoveRange(0, i + expectedLength);
                        return true;
                    }
                }
                // 帧获取失败，记录异常值
                frame = receiveBuffer.ToArray();
            }
            return false;
        }

        private bool TryParseReadCoils(byte[] response,byte slaveID, byte functionCode, ushort expectedLength, out bool[] result, out string err)
        {
            result = null;
            err = null;

            if ((response[1] & 0x80) != 0)
            {
                err = $"ModBus Exception: {response[2]}";
                return false;
            }

            if (response.Length < 5)
            {
                err = $"The length of response is too short. The response length: {response.Length}";
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
            // 重试
            for (int i = 0; i < Config.RetryCount; i++)
            {
                try
                {
                    lock (bufferLock)
                    {
                        receiveBuffer.Clear();  // 清空接收缓冲区
                    }

                    this.serialPort.DiscardInBuffer();  // 清除串口区缓存
                    this.serialPort.Write(request, 0, request.Length);

                    // 拿到解析结果
                    if (ReceiveFrame(request[0], request[1], out var response))
                        // 解析响应数据
                        return parser(response);
                }
                catch (Exception ex)
                {
                    if (i == Config.RetryCount - 1)
                        return Result<T>.Fail(ex.Message);
                }
            }
            return Result<T>.Fail("Failed after retries.");
        }

        public void Dispose()
        {
            serialPort.DataReceived -= SerialPort_OnDataReceived;
            if (serialPort.IsOpen)
                Disconnect();
            serialPort.Dispose();
        }
    }
}
