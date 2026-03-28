using Communication.ModBus.Common;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusResponseParser
    {
        /// <summary>
        /// 解析 ModBus 响应数据，将数据转换为 ushort[] 类型。
        /// </summary>
        /// <param name="response">ModBus 响应数据。</param>
        /// <param name="slaveID">从站 ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="length">读取内容的长度。</param>
        /// <param name="data">需要写入的数据。</param>
        /// <returns>读取到的ushort[] 类型的值。</returns>
        public static Rx<ushort[]> ParseRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? data = null)
        {
            var r = CheckRx(response, slaveID, functionCode, length, data);
            if (!r.IsSuccess)
                return Rx<ushort[]>.Fail(r.ErrorMessage ?? "Check Frame is failed.", r.Data?.Select(b => (ushort)b).ToArray());

            return functionCode switch
            {
                0x01 or 0x02 => Rx<ushort[]>.Success(ParseCoils(response, length)),
                0x03 or 0x04 => Rx<ushort[]>.Success(ParseRegisters(response, length)),
                0x05 or 0x06 => Rx<ushort[]>.Success(response.Select(b => (ushort)b).ToArray()), // 待验证，回显Rx
                0x0F or 0x10 => Rx<ushort[]>.Success(response.Select(b => (ushort)b).ToArray()), // 待增加方法
                _ => Rx<ushort[]>.Fail("The function code not support."),
            };
        }

        public static ushort[] ParseCoils(byte[] response, ushort length)
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

        public static ushort[] ParseRegisters(byte[] response, ushort length)
        {
            ushort[] result = new ushort[length];

            for (int i = 0; i < length; i++)
            {
                int index = 3 + i * 2;

                result[i] = (ushort)((response[index] << 8) | response[index + 1]);
            }

            return result;
        }

        public static Rx<byte[]> CheckRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? data = null)
        {
            if (response == null || response.Length < 5)
                return Rx<byte[]>.Fail("Frame can not be null or frame length < 5", response);

            if (response[0] != slaveID || response[1] != functionCode)
                return Rx<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}", response);

            if ((response[1] & 0x80) != 0)
                return Rx<byte[]>.Fail($"The exception code : {response[2]}", response);

            return functionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(response, functionCode, length),
                0x05 or 0x06 => VerifyEchoRx(response, slaveID, functionCode, data),
                0x0F or 0x10 => VerifyMultiWriteRx(response, slaveID, functionCode, length, data),
                _ => Rx<byte[]>.Fail("The function code not support.", response),
            };
        }

        /// <summary>
        /// 验证 Read Rx，对应 Function Code 0x01, 0x02, 0x03, 0x04。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="length">读取的长度。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyReadRx(byte[] response, int functionCode, ushort length)
        {
            var byteCount = response[2];
            int expectedByteCount;

            if (functionCode == 0x03 || functionCode == 0x04)
                expectedByteCount = length * 2;
            else
                expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
                return Rx<byte[]>.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.", response);

            if (response.Length < 3 + byteCount + 2)
                return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}.", response);

            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 Echo Rx，对应 Function Code 0x05, 0x06。
        /// </summary>
        /// <param name="response">请求数据。</param>
        /// <param name="slaveID">从站 ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="data">写入的数据</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyEchoRx(byte[] response, byte slaveID, int functionCode, byte[]? data = null)
        {
            if (data == null)
                return Rx<byte[]>.Fail("The data is null.");

            if (response.Length < data.Length + 6)
                return Rx<byte[]>.Fail($"The request length is not equal to the data length. Actual {response.Length}, expected {data.Length + 6}.", response);

            if (response[0] != slaveID || response[1] != functionCode)
                return Rx<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}", response);

            for (int i = 4; i < data.Length; i++)
            {
                if (response[i] != data[i - 4])
                    return Rx<byte[]>.Fail($"The data error. Actual {response}, expected {data}.", response);
            }

            return Rx<byte[]>.Success(response);
        }

        public static Rx<byte[]> VerifyMultiWriteRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? data = null)
        {
            return Rx<byte[]>.Success(response);
        }
    }
}
