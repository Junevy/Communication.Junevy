﻿namespace Communication.Modbus.Extensions
{
    /// <summary>
    /// Ushort类型的工具类，提供将ushort转换为字节数组的方法，以及从字节数组转换回ushort的方法。
    /// </summary>
    public static class BinaryExtensions
    {
        /// <summary>
        /// 获取ushort值的字节数组，低字节在前，高字节在后。
        /// </summary>
        /// <param name="value">需要转为byte[]类型的ushort值</param>
        /// <returns></returns>
        public static byte[] ToLittleEndian(this ushort value)
        {
            return
            [
                (byte)(value & 0x00FF), // 低字节
                (byte)((value >> 8) & 0x00FF) // 高字节
            ];
        }

        /// <summary>
        /// 获取ushort值的字节数组，高字节在前，低字节在后
        /// </summary>
        /// <param name="value">需要转为byte[]类型的ushort值</param>
        /// <returns></returns>
        public static byte[] ToBigEndian(this ushort value)
        {
            return
            [
                (byte)((value >> 8) & 0xFF), // 高字节
                (byte)(value & 0xFF)         // 低字节
            ];
        }

        /// <summary>
        /// 将两个字节转为ushort值，高字节在前，低字节在后
        /// </summary>
        /// <param name="lowByte">低字节</param>
        /// <param name="highByte">高字节</param>
        /// <returns>转后的ushort值</returns>
        public static ushort ToUshort(byte lowByte, byte highByte) => (ushort)((highByte << 8) | lowByte);


        /// <summary>
        /// 获取ushort值的字节数组，高字节在前，低字节在后。
        /// </summary>
        /// <param name="ushorts">需要转为byte[]类型的ushort值数组</param>
        /// <param name="reject0x00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的byte[]值</returns>
        public static byte[] ToBigEndianByteArray(this ushort[] ushorts, bool reject0x00 = false)
        {
            if (ushorts == null || ushorts.Length == 0) return [];

            byte[] bytes = new byte[ushorts.Length * 2];

            for (int i = 0; i < ushorts.Length; i++)
            {
                bytes[i * 2] = (byte)(ushorts[i] >> 8);    // 高字节
                bytes[i * 2 + 1] = (byte)(ushorts[i] & 0xFF);  // 低字节
            }

            if (reject0x00)
            {
                bytes = bytes.Where(b => b != 0x00).ToArray();
            }

            return bytes;
        }

        /// <summary>
        /// 将ushort值数组转换为十六进制字符串，高字节在前，低字节在后。
        /// </summary>
        /// <param name="ushorts">需要转为十六进制字符串的ushort值数组</param>
        /// <param name="reject0x00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的十六进制字符串</returns>
        public static string ToHexString(this ushort[] ushorts, bool reject0x00 = false) => ToHexString(ushorts.ToBigEndianByteArray(), reject0x00);

        /// <summary>
        /// 将byte数组转为十六进制字符串，高字节在前，低字节在后
        /// </summary>
        /// <param name="bytes">需要转为字符串的字节数组</param>
        /// <param name="reject0x00">是否拒绝0x00在高字节位</param>
        /// <returns>转后的十六进制字符串</returns>
        public static string ToHexString(this byte[] bytes, bool reject0x00 = false)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            if (reject0x00)
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
        }


    }
}
