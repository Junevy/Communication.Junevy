namespace Communication.ModBus.Utils
{
    /// <summary>
    /// Ushort类型的工具类，提供将ushort转换为字节数组的方法，以及从字节数组转换回ushort的方法。
    /// </summary>
    public static class UshortHelper
    {
        /// <summary>
        /// 获取ushort值的字节数组，低字节在前，高字节在后。
        /// </summary>
        /// <param name="value">需要转为byte[]类型的ushort值</param>
        /// <returns></returns>
        public static byte[] ToBytesByLittleEndian(ushort value)
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
        /// <param name="ushorts">需要转为byte[]类型的ushort值</param>
        /// <returns>转后的byte[]值</returns>
        public static byte[] UShortsToByteArrayBigEndian(this ushort[] ushorts)
        {
            if (ushorts == null || ushorts.Length == 0) return [];

            byte[] bytes = new byte[ushorts.Length * 2];

            for (int i = 0; i < ushorts.Length; i++)
            {
                bytes[i * 2] = (byte)(ushorts[i] >> 8);    // 高字节
                bytes[i * 2 + 1] = (byte)(ushorts[i] & 0xFF);  // 低字节
            }

            return bytes;
        }

        public static string UShortsToHexString(this ushort[] ushorts, bool reject0X00 = true)
        {
            return BytesToHexString(ushorts.UShortsToByteArrayBigEndian(), reject0X00);
        }

        public static string BytesToHexString(this byte[] bytes, bool reject0X00 = true)
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
                return BitConverter.ToString([..buffer]);
            }
        
            // string format = withSpace ? "X2 " : "X2";
            return BitConverter.ToString(bytes);

            // 或者用 LINQ：
            // return string.Join(withSpace ? " " : "", bytes.Select(b => b.ToString("X2")));
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
        /// 将两个字节转为ushort值
        /// </summary>
        /// <param name="lowByte">低字节</param>
        /// <param name="highByte">高字节</param>
        /// <returns></returns>
        public static ushort ToUInt16(byte lowByte, byte highByte)
        {
            return (ushort)((highByte << 8) | lowByte);
        }
    }
}
