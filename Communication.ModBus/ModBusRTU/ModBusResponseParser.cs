using Communication.ModBus.Common;

namespace Communication.ModBus.ModBusRTU
{
    internal class ModBusResponseParser
    {
        public static Result<ushort[]> ParseReadBytes(byte[] response, byte slaveID, int functionCode, ushort length)
        {
            var r = CheckFrame(response, slaveID, functionCode, length);
            if (!r.IsSuccess)
                return Result<ushort[]>.Fail("Verify Frame is failed.");

            return functionCode switch
            {
                0x01 => Result<ushort[]>.Success(ParseCoils(response, length)),
                0x03 => Result<ushort[]>.Success(ParseRegisters(response, length)),
                _ => Result<ushort[]>.Fail("The function code not support."),
            };
        }

        private static ushort[] ParseCoils(byte[] response, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                int byteIndex = i / 8;     // 第几个字节
                int bitIndex = i % 8;      // 第几位（低位在前）
                                           // 获取 0 或 1
                result[i] = (ushort)((response[3 + byteIndex] >> bitIndex) & 0x01);
            }

            return result;
        }

        private static ushort[] ParseRegisters(byte[] response, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                int index = 3 + i * 2;

                result[i] = (ushort)((response[index] << 8) | response[index + 1]);
            }

            return result;
        }

        public static Result<byte[]> CheckFrame(byte[] response, byte slaveID, int functionCode, ushort length)
        {
            if (response == null || response.Length < 5)
                return Result<byte[]>.Fail("Frame can not be null or frame length < 5");

            if (response[0] != slaveID || response[1] != functionCode)
                return Result<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}");

            if ((response[1] & 0x80) != 0)
                return Result<byte[]>.Fail($"The exception code : {response[2]}");

            var byteCount = response[2];
            var expectedByteCount = 0;

            if (functionCode == 0x03 || functionCode == 0x04)
                expectedByteCount = length * 2;
            else
                expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
                return Result<byte[]>.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.");

            if (response.Length < 3 + byteCount + 2)
                return Result<byte[]>.Fail($"Invalid response length. Actual {response.Length}.");

            return Result<byte[]>.Success(response);
        }
    }
}
