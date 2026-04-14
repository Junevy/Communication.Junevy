using Communication.ModBus.Core;

namespace Communication.ModBus.Utils
{
    public static class ModBusTools
    {
        public const int MODBUS_PORT = 502;

        /// <summary>
        /// 构建ModBus发送帧。
        /// </summary>
        /// <param name="tx">ModBus发送请求</param>
        /// <returns>ModBus发送帧</returns>
        /// <exception cref="ArgumentException">当功能码为0x05、0x06、0x0F、0x10、0x17时，且没有提供数据时，抛出异常。</exception>
        public static byte[] BuildTxFrame(Tx tx)
        {
            List<byte> frame = [];

            if (tx.FunctionCode >= ModBusFunctionCode.WriteCoils)
            {
                if (tx.Data == null || tx.Data.Length <= 0)
                {
                    throw new ArgumentException("The data is empty.");
                }

                // 构建写入帧（单个写入）
                if (tx.FunctionCode == ModBusFunctionCode.WriteCoils || tx.FunctionCode == ModBusFunctionCode.WriteHodingRegister)
                    frame =
                    [
                        (byte) tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. tx.Data,
                    ];

                // 构建写入帧（多个写入）
                else
                    frame =
                    [
                        (byte) tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. BitExtentions.ToBytesByBigEndian(tx.Length),
                        (byte)  (tx.FunctionCode == ModBusFunctionCode.WriteMultiCoils
                                    ? (tx.Length + 7) / 8 : (tx.Length * 2) ),
                        .. tx.Data,
                    ];
            }

            // 构建读取帧
            else
            {
                frame =
                [
                    (byte) tx.SlaveId,
                    (byte) tx.FunctionCode,
                    .. BitExtentions.ToBytesByBigEndian(tx.Start),
                    .. BitExtentions.ToBytesByBigEndian(tx.Length),
                ];
            }

            if (frame.Count == 0)
                throw new ArgumentException("Check the function code or data.");

            CRC16.AddCRC16(frame);
            return [.. frame];
        }

        /// <summary>
        /// 解析ModBus接收帧中的线圈数据。
        /// </summary>
        /// <param name="rx">ModBus接收帧</param>
        /// <param name="length">读取线圈数量</param>
        /// <returns>读取到的线圈数据</returns>
        public static ushort[] ParseCoils(byte[] rx, int length)
        {
            ushort[] result = new ushort[length];

            for (ushort i = 0; i < length; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;

                result[i] = (ushort)((rx[3 + byteIndex] >> bitIndex) & 1);
            }            

            return result;
        }

        /// <summary>
        /// 解析ModBus接收帧中的寄存器数据。
        /// </summary>
        /// <param name="rx">ModBus接收帧</param>
        /// <param name="length">读取寄存器数量</param>
        /// <returns>读取到的寄存器数据</returns>
        public static ushort[] ParseRegisters(byte[] rx, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                var index = 3 + i * 2;
                result[i] = (ushort)((rx[index] << 8) | rx[index + 1]);
            }
            return result;
        }

        public static bool ValidateAddress(string address)
        {
            if (address == null || string.IsNullOrEmpty(address))
                return false;

            return true;
        }

        public static bool ValidatePort(int port)
        {
            if (port == MODBUS_PORT) return true;

            if (port <= 1024 || port > 65535)
                return false;

            return true;
        }
    }
}
