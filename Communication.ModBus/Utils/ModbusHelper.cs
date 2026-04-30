using Communication.Modbus.Core;
using Communication.Modbus.Extensions;


namespace Communication.Modbus.Utils
{
    public static class ModbusHelper
    {
        /// <summary>
        /// 检查ModBus发送请求帧是否有效
        /// </summary>
        /// <param name="request">ModBus发送请求帧对象</param>
        /// <returns>是否有效</returns>
        public static bool CheckRequest(ModbusRequest request)
        {
            if (request.Start < 0
                || request.Start > 0xFFFF
                || request.Length < 0
                || request.Length > 0xFFFF
                || request.SlaveId < 0 || request.SlaveId > 255
                || request.FunctionCode < ModbusFunctionCode.ReadCoils
                || request.FunctionCode > ModbusFunctionCode.WriteMultiHodingRegisters)
                return false;

            if (request.FunctionCode < ModbusFunctionCode.WriteCoil ||
                request.FunctionCode > ModbusFunctionCode.WriteMultiHodingRegisters) return true;
            return request.Data != null && request.Data.Length > 0;
        }

        /// <summary>
        /// 构建ModBus发送帧
        /// </summary>
        /// <param name="request">ModBus发送请求帧对象</param>
        /// <returns>ModBus发送帧</returns>
        /// <exception cref="InvalidDataException">当Tx无效时抛出异常</exception>
        public static byte[] BuildRequestFrame(ModbusRequest request)
        {
            if (!CheckRequest(request))
                throw new InvalidDataException("Invalid Tx.");

            return request.ProtocolType switch
            {
                ModbusProtocolType.RTU => BuildRtuRequestFrame(request),
                ModbusProtocolType.TCP => BuildTcpRequestFrame(request),
                _ => throw new InvalidDataException("The protocol is not supported."),
            };
        }

        private static byte[] BuildRtuRequestFrame(ModbusRequest request)
        {
            List<byte> frame;

            if (request.FunctionCode >= ModbusFunctionCode.WriteCoil)
            {
                if (request.Data == null || request.Data.Length <= 0)
                {
                    throw new ArgumentException("The data is empty.");
                }

                // 构建写入帧（单个写入）
                if (request.FunctionCode == ModbusFunctionCode.WriteCoil || request.FunctionCode == ModbusFunctionCode.WriteHodingRegister)
                    frame =
                    [
                        request.SlaveId,
                        (byte) request.FunctionCode,
                        .. request.Start.ToBigEndian(),
                        .. request.Data,
                    ];

                // 构建写入帧（多个写入）
                else
                    frame =
                    [
                        request.SlaveId,
                        (byte) request.FunctionCode,
                        .. request.Start.ToBigEndian(),
                        .. request.Length.ToBigEndian(),
                        (byte)  (request.FunctionCode == ModbusFunctionCode.WriteMultiCoils
                                    ? (request.Length + 7) / 8 : (request.Length * 2) ),
                        .. request.Data,
                    ];
            }

            // 构建读取帧
            else
            {
                frame =
                [
                    request.SlaveId,
                    (byte) request.FunctionCode,
                    .. request.Start.ToBigEndian(),
                    .. request.Length.ToBigEndian(),
                ];
            }

            if (frame.Count == 0)
                throw new ArgumentException("Check the function code or data.");

            Crc16Helper.AddCrc16(frame);
            return [.. frame];
        }

        private static byte[] BuildTcpRequestFrame(ModbusRequest request)
        {
            var baseFrame = BuildRtuRequestFrame(request);
            request.ByteCount = (ushort)(baseFrame.Length - 2);
            var transactionId = (ushort)(request.TransactionId + 0x01);

            List<byte> frame =
                [
                .. transactionId.ToBigEndian(),
                0x00,
                0x00,
                .. request.ByteCount.ToBigEndian(),
                .. baseFrame.Take(baseFrame.Length - 2)
            ];

            return [.. frame];
        }


        /// <summary>
        /// 解析ModBus接收帧中的线圈数据
        /// </summary>
        /// <param name="response">ModBus接收帧</param>
        /// <param name="length">读取线圈数量</param>
        /// <returns>读取到的线圈数据</returns>
        public static bool[] ParseCoils(byte[] response, int length)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response), "The rx data cannot be null.");

            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0.");

            int expectedByteCount = (length + 7) / 8;
            if (response.Length < 2 + expectedByteCount)
                throw new ArgumentException("The rx data is not enough for the requested length.", nameof(response));

            bool[] result = new bool[length];
            var start = 3;

            for (int i = 0; i < length; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;

                result[i] = ((response[start + byteIndex] >> bitIndex) & 1) == 1;
            }

            return result;
        }

        /// <summary>
        /// 解析ModBus接收帧中的寄存器数据。
        /// </summary>
        /// <param name="response">ModBus接收帧</param>
        /// <param name="length">读取寄存器数量</param>
        /// <returns>读取到的寄存器数据</returns>
        public static ushort[] ParseRegisters(byte[] response, int length)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response), "The rx data cannot be null.");

            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0.");

            if (response.Length < 3 + length * 2)
                throw new ArgumentException("The rx data is not enough for the requested length.", nameof(response));

            ushort[] result = new ushort[length];

            for (int i = 0; i < length * 2; i += 2)
            {
                var index = 3 + i;
                result[i / 2] = (ushort)((response[index] << 8) | response[index + 1]);
            }
            return result;
        }

        public static bool VerifyAddress(string address) => !(address == null || string.IsNullOrEmpty(address));

        public static bool VerifyPort(int port) => port == 502 || (port <= 1024 && port <= 65535);

    }
}
