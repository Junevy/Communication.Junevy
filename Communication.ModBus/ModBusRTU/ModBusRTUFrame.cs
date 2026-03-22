using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusRTU
{
    internal static class ModBusRTUFrame
    {
        public static bool TryExtractResponseFrame(List<byte> buffer, byte slaveID, byte functionCode, out byte[] frame)
        {
            frame = Array.Empty<byte>();

            if (buffer.Count < 5)
                return false;

            for (int i = 0; i <= buffer.Count -5; i++)
            {
                byte id = buffer[i];
                byte funcCode = buffer[i + 1];

                // 异常响应
                if (id == slaveID && funcCode == (functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (i + exceptionLength > buffer.Count)
                        return false;

                    var candidate = buffer.Skip(i).Take(exceptionLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, i + exceptionLength);
                        frame = candidate;
                        return true;
                    }

                    buffer.RemoveAt(i); // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if ( (id == slaveID && functionCode == funcCode) 
                    && (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04))
                {
                    int byteCount = buffer[i + 2];
                    var expectedLength = 3 + byteCount + 2;

                    if (buffer.Count < i + expectedLength)
                        return false;

                    var candidate = buffer.Skip(i).Take(expectedLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, i + expectedLength);
                        frame = candidate;
                        return true;
                    }
                    buffer.RemoveAt(i);
                    continue;
                }

                //Write and Read
                if ((id == slaveID && functionCode == funcCode)
                    && (functionCode == 0x05 || functionCode == 0x06 || functionCode == 0x0F || functionCode == 0x10))
                {
                    var expectedLength = 8;

                    if (i + expectedLength > buffer.Count)
                        return false;

                    var candidate = buffer.Skip(i).Take(expectedLength).ToArray();

                    if (ModBusHelper.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, i + expectedLength);
                        frame = candidate;
                        return true;
                    }
                    buffer.RemoveAt(i);
                    continue;
                }

                buffer.RemoveAt(i);
                continue;
            }

            if (buffer.Count > 1024)
            {
                buffer.RemoveRange(0, buffer.Count - 256);
            }

            return false;
        }
    }
}
