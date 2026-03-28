using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusRTU
{
    public static class ModBusRTUFrame
    {
        /// <summary>
        /// 尝试提取响应报文。
        /// </summary>
        /// <param name="buffer">缓存区。</param>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="frame">提取到的报文。</param>
        /// <returns>是否成功提取。</returns>
        public static bool TryExtractResponseFrame(List<byte> buffer, byte slaveID, byte functionCode, out byte[] frame)
        {
            frame = [];

            if (buffer.Count < 5)
                return false;

            //int i = 0;
            while (buffer.Count >= 5)
            {
                byte id = buffer[0];
                byte funcCode = buffer[1];

                if (id != slaveID)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                // 异常响应
                if (funcCode == (functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (exceptionLength > buffer.Count)
                        return false;

                    var candidate = buffer.Take(exceptionLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, exceptionLength);
                        frame = candidate;
                        return true;
                    }

                    buffer.RemoveAt(0); // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if ( id == slaveID && functionCode == funcCode 
                    && (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04))
                {
                    int byteCount = buffer[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (buffer.Count < expectedLength)
                        return false;

                    var candidate = buffer.Take(expectedLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, expectedLength);
                        frame = candidate;
                        return true;
                    }
                    buffer.RemoveAt(0);
                    continue;
                }

                //Write and Read
                if ((id == slaveID && functionCode == funcCode)
                    && (functionCode == 0x05 || functionCode == 0x06 || functionCode == 0x0F || functionCode == 0x10))
                {
                    var expectedLength = 8;

                    if (expectedLength > buffer.Count)
                        return false;

                    var candidate = buffer.Take(expectedLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, expectedLength);
                        frame = candidate;
                        return true;
                    }
                    buffer.RemoveAt(0);
                    continue;
                }

                // 当ID匹配，但是功能码不匹配时，其实这部分还能有点补充，例如 0x07， 0x08， 0x14， 0x15等
                buffer.RemoveAt(0);
                continue;
            }

            if (buffer.Count > 1024)
                buffer.RemoveRange(0, buffer.Count - 256);

            return false;
        }
    }
}
