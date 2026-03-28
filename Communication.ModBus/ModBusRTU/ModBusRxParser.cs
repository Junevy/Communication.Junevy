using Communication.ModBus.Common;
using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRxParser
    {
        /// <summary>
        /// 解析 ModBus 响应数据，将数据转换为 ushort[] 类型。
        /// </summary>
        /// <param name="response">ModBus 响应数据。</param>
        /// <param name="slaveID">从站 ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="length">读取内容的长度。</param>
        /// <param name="writeData">需要写入的数据。</param>
        /// <returns>读取到的ushort[] 类型的值。</returns>
        public static Rx<ushort[]> ParseRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? writeData = null)
        {
            var r = CheckRx(response, slaveID, functionCode, length, writeData);
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

        public static Rx<byte[]> CheckRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? writeData = null)
        {
            if (response == null || response.Length < 5)
                return Rx<byte[]>.Fail("Frame can not be null or frame length < 5", response);

            if ((response[1] & 0x80) != 0)
                return Rx<byte[]>.Fail($"The exception code : {response[2]}", response);

            if (response[0] != slaveID || response[1] != functionCode)
                return Rx<byte[]>.Fail($"The responsed slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}", response);


            return functionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(response, functionCode, length),
                0x05 or 0x06 => VerifyEchoRx(response, slaveID, functionCode, writeData),
                0x0F or 0x10 => VerifyMultiWriteRx(response, slaveID, functionCode, length, writeData),
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
        /// <param name="writeData">写入的数据</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyEchoRx(byte[] response, byte slaveID, int functionCode, byte[]? writeData = null)
        {
            if (writeData == null)
                return Rx<byte[]>.Fail("The data is null.");

            if (response.Length < writeData.Length + 6)
                return Rx<byte[]>.Fail($"The request length is not equal to the data length. Actual {response.Length}, expected {writeData.Length + 6}.", response);

            if (response[0] != slaveID || response[1] != functionCode)
                return Rx<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {slaveID}, {functionCode}", response);

            for (int i = 4; i < writeData.Length; i++)
            {
                if (response[i] != writeData[i - 4])
                    return Rx<byte[]>.Fail($"The data compared error. Actual {response}, expected {writeData}.", response);
            }

            return Rx<byte[]>.Success(response);
        }

        public static Rx<byte[]> VerifyMultiWriteRx(byte[] response, byte slaveID, int functionCode, ushort length, byte[]? data = null)
        {
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 尝试提取标准格式响应报文，提取后进行校验
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

                    if (CRC16.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, exceptionLength);
                        frame = candidate;
                        return true;
                    }

                    buffer.RemoveAt(0); // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if (id == slaveID && functionCode == funcCode
                    && (functionCode == 0x01 || functionCode == 0x02 || functionCode == 0x03 || functionCode == 0x04))
                {
                    int byteCount = buffer[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (buffer.Count < expectedLength)
                        return false;

                    var candidate = buffer.Take(expectedLength).ToArray();

                    if (CRC16.ValidateCRC(candidate))
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

                    if (CRC16.ValidateCRC(candidate))
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
