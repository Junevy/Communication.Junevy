using System.Buffers.Binary;

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

        public static ushort[] ToUShortArray(byte[] bytes)
        {
            int len = bytes.Length / 2;
            ushort[] result = new ushort[len];

            for (int i = 0; i < len; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt16BigEndian(
                    bytes.AsSpan(i * 2, 2));
            }

            return result;
        }
    }
}
