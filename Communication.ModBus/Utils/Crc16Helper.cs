using Communication.Modbus.Extensions;

namespace Communication.Modbus.Utils
{
    /// <summary>
    /// CRC16算法的工具类，提供计算CRC16校验码的方法，以及获取CRC16校验码的高字节和低字节的方法。
    /// </summary>
    public static class Crc16Helper
    {
        /// <summary>
        /// 验证byte[] 值的CRC16校验码是否正确
        /// </summary>
        /// <param name="frame">需要被验证CRC16校验码的byte[]类型的值</param>
        /// <returns>bool类型的校验结果</returns>
        public static bool VerifyCrc(byte[] frame)
        {
            var dataWithoutCRC = frame.Take(frame.Length - 2).ToArray();
            var receivedCRC = frame.Skip(frame.Length - 2).ToArray();
            var calculatedCRC = CrcLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }


        /// <summary>
        /// 验证ReadOnlySpan<byte> 值的CRC16校验码是否正确
        /// </summary>
        /// <param name="frame">需要被验证CRC16校验码的ReadOnlySpan<byte>类型的值</param>
        /// <returns>bool类型的校验结果</returns>
        public static bool VerifyCrc(ReadOnlySpan<byte> frame)
        {
            var dataWithoutCRC = frame.Slice(0, frame.Length - 2);
            var receivedCRC = frame.Slice(frame.Length - 2, frame.Length);
            var calculatedCRC = CrcLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }

        /// <summary>
        /// 向byte[] 值的末尾添加CRC16校验码，默认使用小端序
        /// </summary>
        /// <param name="frame">需要被添加CRC16校验码的byte[]类型的值</param>
        public static void AddCrc16(List<byte> frame)
            => frame.AddRange(CrcLittleEndian([.. frame]));

        /// <summary>
        /// 计算byte[] 值的CRC16校验码
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns>ushort类型的CRC16校验码</returns>
        public static ushort ComputeCrc(byte[] data)
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

        public static ushort ComputeCrc(ReadOnlySpan<byte> data)
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
        /// 计算byte[] 值的CRC16校验码，并按照小端序返回
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns>byte[]类型的CRC16校验码，按照小端序返回</returns>
        public static byte[] CrcLittleEndian(byte[] data)
        {
            ushort crc = ComputeCrc(data);
            return crc.ToLittleEndian(); // 取低字节
        }

        public static byte[] CrcLittleEndian(ReadOnlySpan<byte> data)
        {
            ushort crc = ComputeCrc(data);
            return crc.ToLittleEndian(); // 取低字节
        }

        /// <summary>
        /// 计算byte[] 值的CRC16校验码，并按照大端序返回
        /// </summary>
        /// <param name="data">需要被计算CRC16的byte[]类型的值</param>
        /// <returns>byte[]类型的CRC16校验码，按照大端序返回</returns>
        public static byte[] CrcBigEndian(byte[] data)
        {
            ushort crc = ComputeCrc(data);
            return BinaryExtensions.ToBigEndian(crc); // 取高字节
        }
    }
}
