using Communication.ModBus.Common;
using System.ComponentModel.DataAnnotations;

namespace Communication.ModBus.Utils
{
    public static class ModBusTools
    {
        /// <summary>
        /// 构建ModBus发送帧。
        /// </summary>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取长度</param>
        /// <param name="data">数据</param>
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

                if (tx.FunctionCode == ModBusFunctionCode.WriteCoils || tx.FunctionCode == ModBusFunctionCode.WriteHodingRegister)
                    frame =
                    [
                        (byte) tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. tx.Data,
                    ];
                else
                    frame =
                    [
                        (byte) tx.SlaveId,
                        (byte) tx.FunctionCode,
                        .. BitExtentions.ToBytesByBigEndian(tx.Start),
                        .. BitExtentions.ToBytesByBigEndian(tx.Length),
                        (byte) ( (tx.FunctionCode == ModBusFunctionCode.WriteMultiCoils
                                    ? (tx.Length + 7) / 8 : (tx.Length * 2) )),
                        .. tx.Data,
                    ];
            }
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


            /*
            //if (tx.Data == null || tx.Data.Length <= 0)
            //{
            //    // 写操作，但没有数据提供
            //    if (tx.FunctionCode >= ModBusFunctionCode.WriteCoils)
            //    {
            //        throw new ArgumentException("The data is empty.");
            //    }

            //    // 读操作不需要数据
            //    if (tx.FunctionCode < ModBusFunctionCode.WriteCoils)
            //    {
            //        frame =
            //        [
            //            (byte) tx.SlaveId,
            //            (byte) tx.FunctionCode,
            //            .. BitExtentions.ToBytesByBigEndian(tx.Start),
            //            .. BitExtentions.ToBytesByBigEndian(tx.Length),
            //        ];
            //    }
            //}
            //else
            //{
            //    // 写操作，有数据提供
            //    if (tx.FunctionCode >= ModBusFunctionCode.WriteCoils)
            //    {
            //        frame =
            //        [
            //            (byte) tx.SlaveId,
            //            (byte) tx.FunctionCode,
            //            .. BitExtentions.ToBytesByBigEndian(tx.Start),
            //            .. tx.Data ?? [],
            //        ];
            //    }

            //    /* 读操作，有数据，但功能码为0x01、0x02、0x03、0x04时，忽略数据。（废弃）
            //    // 防止误读取，导致数据错误。
            //    // 例如：提供数据情况下，但功能码写错（0x05 => 0x01）：
            //    // 01 01 01 00 01 01 CRC  
            //    // 正常需求（读从站1，地址0x01，读取1个线圈）：
            //    // 01 01 00 00 00 01 CRC  
            //    // 所以稳妥考虑，注释该选项
            //    // if (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04)
            //    // {
            //    //     frame =
            //    //     [
            //    //         slaveID,
            //    //         functionCode,
            //    //         .. UshortHelper.ToBytesByBigEndian(start),
            //    //         .. UshortHelper.ToBytesByBigEndian(length),
            //    //     ];
            //    }*/
            //} */

            if (frame.Count == 0)
                throw new ArgumentException("Check the function code or data.");

            CRC16.AddCRC16(frame);
            return [.. frame];
        }

        public static ushort[] ParseCoils(byte[] rx, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                int byteIndex = i / 8;     // 第几个字节
                int bitIndex = i % 8;      // 第几位（低位在前）
                                           // 获取 0 或 1
                result[i] = (ushort)((rx[3 + byteIndex] >> bitIndex) & 0x01);
            }
            return result;
        }

        public static ushort[] ParseRegisters(byte[] rx, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                int index = 3 + i * 2;

                result[i] = (ushort)((rx[index] << 8) | rx[index + 1]);
            }
            return result;
        }
    }
}
