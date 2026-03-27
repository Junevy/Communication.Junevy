namespace Communication.ModBus.Utils
{
    public static class ModBusHelper
    {
        public static bool ValidateCRC(byte[] frame)
        {
            var dataWithoutCRC = frame.Take(frame.Length - 2).ToArray();
            var receivedCRC = frame.Skip(frame.Length - 2).ToArray();
            var calculatedCRC = CRC16.CRCLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }

        public static void AddCRC16(List<byte> frame)
            => frame.AddRange(CRC16.CRCLittleEndian(frame.ToArray()));

        // public static byte[] BuildReadFrame(byte slaveID, byte functionCode, ushort start, ushort length)
        // {
        //     List<byte> frame =
        //     [
        //         slaveID,
        //         functionCode,
        //         .. UshortHelper.ToBytesByBigEndian(start),
        //         .. UshortHelper.ToBytesByBigEndian(length),
        //     ];

        //     AddCRC16(frame);
        //     return frame.ToArray();
        // }

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
        public static byte[] BuildTxFrame(byte slaveID, byte functionCode, ushort start, ushort length, byte[]? data = null)
        {
            List<byte> frame = [];

            if (data == null || data.Length <= 0)
            {
                // 写操作，但没有数据提供
                if (functionCode == 0x05 || functionCode == 0x06
                || functionCode == 0x0F || functionCode == 0x10 || functionCode == 0x17)
                {
                    throw new ArgumentException("The data is empty.");
                }

                // 读操作不需要数据
                if (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04)
                {
                    frame =
                    [
                        slaveID,
                        functionCode,
                        .. UshortHelper.ToBytesByBigEndian(start),
                        .. UshortHelper.ToBytesByBigEndian(length),
                    ];
                }
            }
            else
            {
                // 写操作，有数据提供
                if (functionCode == 0x05 || functionCode == 0x06
                || functionCode == 0x0F || functionCode == 0x10 || functionCode == 0x17)
                {
                    frame =
                    [
                        slaveID,
                        functionCode,
                        .. data ?? [],
                    ];
                }

                /* 读操作，有数据，但功能码为0x01、0x02、0x03、0x04时，忽略数据。（废弃）
                // 防止误读取，导致数据错误。
                // 例如：提供数据情况下，但功能码写错（0x05 => 0x01）：
                // 01 01 01 00 01 01 CRC  
                // 正常需求（读从站1，地址0x01，读取1个线圈）：
                // 01 01 00 00 00 01 CRC  
                // 所以稳妥考虑，注释该选项
                // if (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04)
                // {
                //     frame =
                //     [
                //         slaveID,
                //         functionCode,
                //         .. UshortHelper.ToBytesByBigEndian(start),
                //         .. UshortHelper.ToBytesByBigEndian(length),
                //     ];
                }*/
            }

            if (frame.Count == 0)
                throw new ArgumentException("Check the function code or data.");

            AddCRC16(frame);
            return [.. frame];
        }
    }
}
