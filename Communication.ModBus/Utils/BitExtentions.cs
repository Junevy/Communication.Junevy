namespace Communication.Modbus.Utils
{
    /// <summary>
    /// Ushort类型的工具类，提供将ushort转换为字节数组的方法，以及从字节数组转换回ushort的方法。
    /// </summary>
    public static class BitExtentions
    {
        /// <summary>
        /// 获取ushort值的字节数组，低字节在前，高字节在后。
        /// </summary>
        /// <param name="value">需要转为byte[]类型的ushort值</param>
        /// <returns></returns>
        public static byte[] ToBytesByLittleEndian(this ushort value)
        {
            return
            [
                (byte)(value & 0x00FF), // 低字节
                (byte)((value >> 8) & 0x00FF) // 高字节
            ];
        }

        /// <summary>
        /// 获取ushort值的字节数组，高字节在前，低字节在后。
        /// </summary>
        /// <param name="value">需要转为byte[]类型的ushort值</param>
        /// <returns></returns>
        public static byte[] ToBytesByBigEndian(ushort value)
        {
            return
            [
                (byte)((value >> 8) & 0xFF), // 高字节
                (byte)(value & 0xFF)         // 低字节
            ];
        }

        /// <summary>
        /// 将两个字节转为ushort值，高字节在前，低字节在后。
        /// </summary>
        /// <param name="lowByte">低字节</param>
        /// <param name="highByte">高字节</param>
        /// <returns>转后的ushort值</returns>
        public static ushort ToUshort(byte lowByte, byte highByte)
        {
            return (ushort)((highByte << 8) | lowByte);
        }


        /// <summary>
        /// 获取ushort值的字节数组，高字节在前，低字节在后。
        /// </summary>
        /// <param name="ushorts">需要转为byte[]类型的ushort值数组</param>
        /// <param name="reject0X00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的byte[]值</returns>
        public static byte[] ToByteArrayBigEndian(this ushort[] ushorts, bool reject0X00 = false)
        {
            if (ushorts == null || ushorts.Length == 0) return [];

            byte[] bytes = new byte[ushorts.Length * 2];

            for (int i = 0; i < ushorts.Length; i++)
            {
                bytes[i * 2] = (byte)(ushorts[i] >> 8);    // 高字节
                bytes[i * 2 + 1] = (byte)(ushorts[i] & 0xFF);  // 低字节
            }

            if (reject0X00)
            {
                bytes = bytes.Where(b => b != 0x00).ToArray();
            }

            return bytes;
        }

        /// <summary>
        /// 将ushort值数组转换为十六进制字符串，高字节在前，低字节在后。
        /// </summary>
        /// <param name="ushorts">需要转为十六进制字符串的ushort值数组</param>
        /// <param name="reject0X00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的十六进制字符串</returns>
        public static string ToHexString(this ushort[] ushorts, bool reject0X00 = false)
        {
            return ToHexString(ushorts.ToByteArrayBigEndian(), reject0X00);
        }

        /// <summary>
        /// 将字节数组转换为十六进制字符串，高字节在前，低字节在后。
        /// </summary>
        /// <param name="bytes">需要转为十六进制字符串的字节数组</param>
        /// <param name="reject0X00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的十六进制字符串</returns>
        public static string ToHexString(this byte[] bytes, bool reject0X00 = false)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            if (reject0X00)
            {
                List<byte> buffer = [];

                for (int i = 0; i < bytes.Length; i++)
                {
                    if (i % 2 == 1)
                    {
                        buffer.Add(bytes[i]);
                    }
                }
                return BitConverter.ToString([.. buffer]);
            }

            return BitConverter.ToString(bytes);

            // 或者用 LINQ：
            // return string.Join(withSpace ? " " : "", bytes.Select(b => b.ToString("X2")));
        }

        /// <summary>
        /// 将ushort值数组转换为byte[]类型的数组，数组中的每个byte表示8个线圈的状态。
        /// </summary>
        /// <param name="values">需要解析线圈状态的ushort值数组</param>
        /// <returns>解析后的线圈状态数组</returns>
        public static byte[] ToMultiCoils(this ushort[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<byte>();

            int byteCount = (values.Length + 7) / 8;
            byte[] result = new byte[byteCount];

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0) // 非0即ON
                {
                    int byteIndex = i / 8;
                    int bitIndex = i % 8;

                    result[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
            return result;
        }


        /// <summary>
        /// 转换byte[] ：相邻的两个字节任意一个不为0x00，则将两个字节置为0xFF 00，写入线圈时适用
        /// </summary>
        /// <param name="txData">需要处理的线圈状态数组</param>
        /// <returns>处理后的线圈状态数组</returns>
        public static byte[] ToCoils(this byte[] txData)
        {
            if (txData == null || txData.Length % 2 != 0)
                throw new ArgumentException("Tx Data must be even length.");

            for (int i = 0; i < txData.Length; i += 2)
            {
                byte high = txData[i];
                byte low = txData[i + 1];

                if (high != 0 || low != 0)   // 只要有一个不为0
                {
                    txData[i] = 0xFF;
                    txData[i + 1] = 0x00;
                }
                // else: 都是 0x00，什么都不做，保持原样
            }
            return txData;
        }


        // public static string 


    }
}
