namespace Communication.ModBus.Utils
{
    /// <summary>
    /// CRC16算法的工具类，提供计算CRC16校验码的方法，以及获取CRC16校验码的高字节和低字节的方法。
    /// </summary>
    public static class CRC16
    {
        public static bool ValidateCRC(byte[] frame)
        {
            var dataWithoutCRC = frame.Take(frame.Length - 2).ToArray();
            var receivedCRC = frame.Skip(frame.Length - 2).ToArray();
            var calculatedCRC = CRCLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }

        public static void AddCRC16(List<byte> frame)
            => frame.AddRange(CRCLittleEndian(frame.ToArray()));

        /// <summary>
        /// 计算byte[]的CRC16校验码
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns>ushort类型的CRC16校验码</returns>
        public static ushort Compute(byte[] data)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i]; // 异或当前字节

                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001; // 多项式
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }

        /// <summary>
        /// 计算byte[]类型值的CRC16校验码，并按照小端序返回
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns></returns>
        public static byte[] CRCLittleEndian(byte[] data)
        {
            ushort crc = Compute(data);
            return crc.ToBytesByLittleEndian(); // 取低字节
        }

        /// <summary>
        /// 计算byte[]类型值的CRC16校验码，并按照大端序返回
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns></returns>
        public static byte[] CRCBigEndian(byte[] data)
        {
            ushort crc = Compute(data);
            return BitExtentions.ToBytesByBigEndian(crc); // 取高字节
        }
    }
}
