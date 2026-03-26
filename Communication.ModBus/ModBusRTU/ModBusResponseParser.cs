using Communication.ModBus.Common;
using System.Numerics;

namespace Communication.ModBus.ModBusRTU
{
    internal class ModBusResponseParser
    {
        public static Result<byte[]> ParseReadBytes(byte[] response, byte slaveID, int functionCode, ushort length)
        {
            var r = CheckFrame(response, slaveID, functionCode, length);
            if (!r.IsSuccess)
                return Result<byte[]>.Fail("Verify Frame is failed.");

            return functionCode switch
            {
                0x01 => Result<byte[]>.Success(ParseCoils(response, length)),
                0x03 => Result<byte[]>.Success(ParseRegister(response, length)),
                _ => Result<byte[]>.Fail("The function code not support."),
            };
        }

        private static byte[] ParseCoils(byte[] response, ushort length)
        {
            byte[] result = new byte[length];

            for (int i = 0; i < length; i++)
            {
                int byteIndex = i / 8;
                int bitIndex = i % 8;
                result[i] = (byte)((response[3 + byteIndex] & (1 << bitIndex)));

            }
            return result;
        }

        private static byte[] ParseRegister(byte[] response, ushort length)
        {
            var result = new byte[length * 2];
            Array.Copy(response, 3, result, 0, length * 2);    // 该报文2字节起
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
