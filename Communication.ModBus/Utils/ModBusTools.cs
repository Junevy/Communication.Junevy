using System.Buffers;
using System.Buffers.Binary;
using Communication.Modbus.Core;

namespace Communication.Modbus.Utils
{
    public static class ModbusTools
    {
        public static bool CheckTx(ModbusTx tx)
        {
            if (tx.Start < 0 || tx.Start > 0xFFFF
                || tx.Length < 0
                || tx.Length > 0xFFFF
                || tx.SlaveId < 0 || tx.SlaveId > 255
                || tx.FunctionCode < ModbusFunctionCode.ReadCoils
                || tx.FunctionCode > ModbusFunctionCode.WriteMultiHodingRegisters)
                return false;

            if (tx.FunctionCode >= ModbusFunctionCode.WriteCoil && tx.FunctionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
            {
                if (tx.Data == null || tx.Data.Length <= 0)
                    return false;
            }

            if (tx.ProtocolType != 0x0000) return false;

            return true;
        }

        /// <summary>
        /// 构建ModBus发送帧
        /// </summary>
        /// <param name="tx">ModBus发送请求帧对象</param>
        /// <returns>ModBus发送帧</returns>
        /// <exception cref="InvalidDataException">当Tx无效时抛出异常</exception>
        public static byte[] BuildTxFrame(ModbusTx tx)
        {
            if (!CheckTx(tx))
                throw new InvalidDataException("Invalid Tx.");

            if (tx.ProtocolType == ModbusProtocolType.RTU)
                return BuildRTUTxFrame(tx);
            else if (tx.ProtocolType == ModbusProtocolType.TCP)
                return BuildTCPTxFrame(tx);
            else
                throw new InvalidDataException("The protocol is not supported.");
        }

        private static byte[] BuildRTUTxFrame(ModbusTx tx)
        {
            List<byte> frame;

            if (tx.FunctionCode >= ModbusFunctionCode.WriteCoil)
            {
                if (tx.Data == null || tx.Data.Length <= 0)
                {
                    throw new ArgumentException("The data is empty.");
                }

                // 构建写入帧（单个写入）
                if (tx.FunctionCode == ModbusFunctionCode.WriteCoil || tx.FunctionCode == ModbusFunctionCode.WriteHodingRegister)
                    frame =
                    [
                        tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. tx.Data,
                    ];

                // 构建写入帧（多个写入）
                else
                    frame =
                    [
                        tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. BitExtentions.ToBytesByBigEndian(tx.Length),
                        (byte)  (tx.FunctionCode == ModbusFunctionCode.WriteMultiCoils
                                    ? (tx.Length + 7) / 8 : (tx.Length * 2) ),
                        .. tx.Data,
                    ];
            }

            // 构建读取帧
            else
            {
                frame =
                [
                    tx.SlaveId,
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


        private static byte[] BuildTCPTxFrame(ModbusTx tx)
        {
            var baseFrame = BuildRTUTxFrame(tx);
            tx.ByteCount = (ushort)(baseFrame.Length - 2);
            var transactionId = (ushort)(tx.TransactionId + 0x01);

            List<byte> frame =
            [
                .. BitExtentions.ToBytesByBigEndian(transactionId),
                0x00,
                0x00,
                .. BitExtentions.ToBytesByBigEndian(tx.ByteCount),
                .. baseFrame.Take(baseFrame.Length - 2)
            ];

            return frame.ToArray();
        }

        /// <summary>
        /// 解析ModBus接收帧中的线圈数据
        /// </summary>
        /// <param name="rx">ModBus接收帧</param>
        /// <param name="length">读取线圈数量</param>
        /// <returns>读取到的线圈数据</returns>
        public static bool[] ParseCoils(byte[] rx, int length)
        {
            if (rx == null)
                throw new ArgumentNullException(nameof(rx), "The rx data cannot be null.");

            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be greater than 0.");

            int expectedByteCount = (length + 7) / 8;
            if (rx.Length < ModbusParams.RTU_BYTECOUNT_START + expectedByteCount)
                throw new ArgumentException("The rx data is not enough for the requested length.", nameof(rx));

            bool[] result = new bool[length];

            for (int i = 0; i < length; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;

                result[i] = ((rx[ModbusParams.RTU_BYTECOUNT_START + byteIndex] >> bitIndex) & 1) == 1;
            }

            return result;
        }

        /// <summary>
        /// 解析ModBus接收帧中的寄存器数据。
        /// </summary>
        /// <param name="rx">ModBus接收帧</param>
        /// <param name="length">读取寄存器数量</param>
        /// <returns>读取到的寄存器数据</returns>
        public static byte[] ParseRegisters(byte[] rx, ushort length)
        {
            byte[] result = new byte[length];

            for (int i = 0; i < length; i++)
            {
                var index = ModbusParams.RTU_BYTECOUNT_START + i * 2;
                result[i] = (byte)((rx[index] << 8) | rx[index + 1]);
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
            if (port == ModbusParams.TCP_PORT) return true;

            if (port <= 1024 || port > 65535)
                return false;

            return true;
        }



        public static ushort ReadUInt16BigEndian(ReadOnlySequence<byte> seq)
        {
            // 单段快速路径
            if (seq.IsSingleSegment)
                return BinaryPrimitives.ReadUInt16BigEndian(seq.FirstSpan);

            // 跨段安全路径（极少触发）
            Span<byte> tmp = stackalloc byte[2];
            seq.CopyTo(tmp);
            return BinaryPrimitives.ReadUInt16BigEndian(tmp);
        }
    }
}
