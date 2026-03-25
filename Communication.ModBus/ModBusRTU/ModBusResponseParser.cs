using Communication.ModBus.Common;

namespace Communication.ModBus.ModBusRTU
{
    internal class ModBusResponseParser
    {
        public static Result<bool[]> ParseReadCoils(byte[] response, byte slaveID, int functionCode, ushort length)
        {
            if (response == null || response.Length < 5)
                return Result<bool[]>.Fail("Frame can not be null or frame length < 5");

            if (response[0] != slaveID || response[1] != functionCode)
                return Result<bool[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}");

            if ((response[1] & 0x80) != 0)
                return Result<bool[]>.Fail($"The exception code : {response[2]}");

            var byteCount = response[2];
            var expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
                return Result<bool[]>.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.");

            if (response.Length < 3 + byteCount + 2)
                return Result<bool[]>.Fail($"Invalid response length. Actual {response.Length}.");

            var result = new bool[expectedByteCount];

            for (int i = 0; i < byteCount; i++)
            {
                var b = response[3 + i];

                // 每个字节包含8个线圈状态，依次解析
                for (int j = 0; j < expectedByteCount; j++)
                    result[j] = ((b & (1 << j)) != 0);
            }
            return Result<bool[]>.Success(result);
        }
    }
}
