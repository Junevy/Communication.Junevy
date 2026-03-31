using Communication.ModBus.Common;
using Communication.ModBus.Utils;

namespace Communication.ModBus.ModBusRTU
{
    public class ModBusRxParser
    {
        private static readonly ISerilog? logger = Serilogger.Instance;

        /// <summary>
        /// 解析 ModBus 响应的数据
        /// </summary>
        /// <param name="response">ModBus 响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <returns>解析后的响应数据</returns>
        public static Rx<byte[]> ParseRx(byte[] response, Tx tx)
        {
            if (response == null || response.Length < 5)
            {
                logger?.Error("Frame can not be null or frame length < 5, {length}", response?.Length ?? 0);
                return Rx<byte[]>.Fail("Frame can not be null or frame length < 5", response);
            }

            if ((response[1] & 0x80) != 0)
            {
                logger?.Error("The exception code : {code}", response[2]);
                return Rx<byte[]>.Fail($"The exception code : {response[2]}", response);
            }

            if (response[0] != tx.SlaveId || response[1] != (byte)tx.FunctionCode)
            {
                logger?.Error("The responsed slave id or function code error : {slaveId}, {functionCode}", response[0], response[1]);
                logger?.Error("The actual slave id or function code : {slaveId}, {functionCode}", tx.SlaveId, tx.FunctionCode); 
                return Rx<byte[]>.Fail($"The responsed slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {tx.SlaveId}, {tx.FunctionCode}", response);
            }

            logger?.Information("The function code : {functionCode}", tx.FunctionCode);
            return (byte)tx.FunctionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(response, (ushort)tx.FunctionCode, tx.Length),
                0x05 or 0x06 => VerifyEchoRx(response, tx),
                0x0F or 0x10 => VerifyMultiWriteRx(response, tx),
                _ => Rx<byte[]>.Fail("The function code not support.", response),
            };
        }

        /// <summary>
        /// 验证 读取功能的 Rx，对应 Function Code 0x01, 0x02, 0x03, 0x04。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="length">读取的长度。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyReadRx(byte[] response, ushort functionCode, ushort length)
        {
            var byteCount = response[2];
            int expectedByteCount;

            if (functionCode == 0x03 || functionCode == 0x04)
                expectedByteCount = length * 2;
            else
                expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
            {
                logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
                return Rx<byte[]>.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.", response);
            }

            if (response.Length < 3 + byteCount + 2)
            {
                logger?.Error("Invalid response length. Actual {length}", response.Length);
                return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}.", response);
            }

            logger?.Rx("SerialPort", response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 回显 Rx，对应 Function Code 0x05, 0x06。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyEchoRx(byte[] response, Tx tx)
        {
            if (tx.Data == null)
            {
                logger?.Error("The data is null.");
                return Rx<byte[]>.Fail("The data is null.", response);
            }

            if (response.Length < tx.Data.Length + 6)
            {
                logger?.Error("The request length is not equal to the data length. Actual {length}, expected {expectedLength}", response.Length, tx.Data.Length + 6);   
                return Rx<byte[]>.Fail($"The request length is not equal to the data length. Actual {response.Length}, expected {tx.Data.Length + 6}.", response);
            }

            if (response[0] != tx.SlaveId || response[1] != (ushort)tx.FunctionCode)
            {
                logger?.Error("The slave id or function code error : {slaveId}, {functionCode}", response[0], response[1]);
                logger?.Error("The actual slave id or function code : {slaveId}, {functionCode}", tx.SlaveId, tx.FunctionCode); 
                return Rx<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {tx.SlaveId}, {tx.FunctionCode}", response);
            }

            for (int i = 4; i < tx.Data.Length; i++)
            {
                if (response[i] != tx.Data[i - 4])
                {
                    logger?.Error("The data compared error. Actual {data}, expected {expectedData}", response[i], tx.Data[i - 4]);
                    return Rx<byte[]>.Fail($"The data compared error. Actual {response[i]}, expected {tx.Data[i - 4]}.", response);
                }
            }

            logger?.Rx("SerialPort", response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 多写入 Rx，对应 Function Code 0x0F, 0x10。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyMultiWriteRx(byte[] response, Tx tx)
        {
            if (tx.SlaveId != response[0] || response[1] != (byte)tx.FunctionCode)
            {
                logger?.Error("The slave id or function code error : {slaveId}, {functionCode}", response[0], response[1]);
                logger?.Error("The actual slave id or function code : {slaveId}, {functionCode}", tx.SlaveId, tx.FunctionCode); 
                return Rx<byte[]>.Fail($"The slave id or function code error : {response[0]}, {response[1]}. " +
                    $"The actual slave id or function code : {tx.SlaveId}, {tx.FunctionCode}", response);   
            }

            var start = BitExtentions.ToUshort(response[3], response[2]);
            if (start != tx.Start)
            {
                logger?.Error("The start address error. Actual {start}, expected {expectedStart}", start, tx.Start);
                return Rx<byte[]>.Fail($"The start address error. Actual {start}, expected {tx.Start}.", response);
            }

            var length = BitExtentions.ToUshort(response[5], response[4]);
            if (length != tx.Length)
                return Rx<byte[]>.Fail($"The length error. Actual {length}, expected {tx.Length}.", response);

            logger?.Rx("SerialPort", response);
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
        public static bool TryExtractRxFrame(List<byte> buffer, byte slaveID, byte functionCode, out byte[] frame)
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
                    logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.", id, slaveID);
                    buffer.RemoveAt(0);
                    continue;
                }

                // 异常响应
                if (funcCode == (functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (exceptionLength > buffer.Count)
                    {
                        logger?.Error("The exception response length is not matched. Actual {length}, expected {expectedLength} and return false.", buffer.Count, exceptionLength);
                        return false;
                    }

                    var candidate = buffer.Take(exceptionLength).ToArray();
                    logger?.Rx("SerialPort", candidate);

                    if (CRC16.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, exceptionLength);
                        frame = candidate;
                        return true;
                    }

                    logger?.Warning("The exception response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate[4], candidate[3]);
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
                    logger?.Rx("SerialPort", candidate);

                    if (CRC16.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, expectedLength);
                        frame = candidate;
                        return true;
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate[4], candidate[3]);
                    buffer.RemoveAt(0);
                    continue;
                }

                //Write and Read
                if (id == slaveID && functionCode == funcCode
                    && (functionCode == 0x05 || functionCode == 0x06 || functionCode == 0x0F || functionCode == 0x10))
                {
                    var expectedLength = 8;

                    if (expectedLength > buffer.Count)
                        return false;

                    var candidate = buffer.Take(expectedLength).ToArray();
                    logger?.Rx("SerialPort", candidate);

                    if (CRC16.ValidateCRC(candidate))
                    {
                        buffer.RemoveRange(0, expectedLength);
                        frame = candidate;
                        return true;
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate[4], candidate[3]);
                    buffer.RemoveAt(0);
                    continue;
                }


                // 当ID匹配，但是功能码不匹配时，其实这部分还能有点补充，例如 0x07， 0x08， 0x14， 0x15等

                logger?.Warning("Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.", buffer);
                buffer.RemoveAt(0);
                continue;
            }


            if (buffer.Count > 1024)
                buffer.RemoveRange(0, buffer.Count - 256);

            logger?.Error("The Rx match failed {@Buffer}", buffer);
            return false;
        }
    }
}
