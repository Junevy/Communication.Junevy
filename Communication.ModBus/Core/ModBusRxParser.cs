using Communication.ModBus.Common;
using Communication.ModBus.Utils;

namespace Communication.ModBus.Core
{
    public static class ModBusRxParser
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
            // 自动识别协议类型
            var protocolType = DetectProtocolType(response);
            return ParseRx(response, tx, protocolType);
        }

        /// <summary>
        /// 解析 ModBus 响应的数据
        /// </summary>
        /// <param name="response">ModBus 响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <param name="protocolType">ModBus 协议类型。</param>
        /// <returns>解析后的响应数据</returns>
        public static Rx<byte[]> ParseRx(byte[] response, Tx tx, ModbusProtocolType protocolType)
        {
            if (response == null)
            {
                logger?.Error("Frame can not be null");
                return Rx<byte[]>.Fail("Frame can not be null", response);
            }

            // 根据协议类型处理响应数据
            byte[] payload;
            if (protocolType == ModbusProtocolType.TCP)
            {
                // 处理 TCP 报文的 MBAP 头
                var tcpResult = ProcessTcpHeader(response);
                if (!tcpResult.IsSuccess)
                {
                    return tcpResult;
                }
                payload = tcpResult.Data!;
            }
            else
            {
                // RTU 协议直接使用原始数据
                if (response.Length < 5)
                {
                    logger?.Error("Frame length < 5, {length}", response.Length);
                    return Rx<byte[]>.Fail("Frame length < 5", response);
                }
                payload = response;
            }

            // 检查异常响应
            if ((payload[1] & 0x80) != 0)
            {
                logger?.Error("The exception code : {code}", payload[2]);
                return Rx<byte[]>.Fail($"The exception code : {payload[2]}", response);
            }

            // 检查从站ID和功能码
            if (payload[0] != tx.SlaveId || payload[1] != (byte)tx.FunctionCode)
            {
                logger?.Error("The responsed slave id or function code error : {slaveId}, {functionCode}", payload[0], payload[1]);
                logger?.Error("The actual slave id or function code : {slaveId}, {functionCode}", tx.SlaveId, tx.FunctionCode); 
                return Rx<byte[]>.Fail($"The responsed slave id or function code error : {payload[0]}, {payload[1]}. " +
                    $"The actual slave id or function code : {tx.SlaveId}, {tx.FunctionCode}", response);
            }

            logger?.Information("The function code : {functionCode}", tx.FunctionCode);
            return (byte)tx.FunctionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(payload, (ushort)tx.FunctionCode, tx.Length, protocolType),
                0x05 or 0x06 => VerifyEchoRx(payload, tx, protocolType),
                0x0F or 0x10 => VerifyMultiWriteRx(payload, tx, protocolType),
                _ => Rx<byte[]>.Fail("The function code not support.", response),
            };
        }

        /// <summary>
        /// 自动检测 ModBus 协议类型
        /// </summary>
        /// <param name="response">ModBus 响应数据。</param>
        /// <returns>检测到的协议类型。</returns>
        private static ModbusProtocolType DetectProtocolType(byte[] response)
        {
            // TCP 协议有 MBAP 头，长度至少为 7 字节
            // 且前两个字节通常是事务标识符，值一般较小
            if (response != null && response.Length >= 7)
            {
                // 简单检测：如果前两个字节的值较小（通常事务ID不会太大），则认为是TCP协议
                // 更准确的检测可以根据Modbus TCP规范进行
                return ModbusProtocolType.TCP;
            }
            return ModbusProtocolType.RTU;
        }

        /// <summary>
        /// 处理 TCP 报文的 MBAP 头
        /// </summary>
        /// <param name="response">TCP 响应数据。</param>
        /// <returns>处理结果，包含去除 MBAP 头后的 payload。</returns>
        private static Rx<byte[]> ProcessTcpHeader(byte[] response)
        {
            if (response.Length < 7)
            {
                logger?.Error("TCP frame length < 7, {length}", response.Length);
                return Rx<byte[]>.Fail("TCP frame length < 7", response);
            }

            // 提取 MBAP 头信息
            ushort transactionId = BitExtentions.ToUshort(response[1], response[0]);
            ushort protocolId = BitExtentions.ToUshort(response[3], response[2]);
            ushort length = BitExtentions.ToUshort(response[5], response[4]);
            byte unitId = response[6];

            // 验证协议ID（Modbus TCP 协议ID为0）
            if (protocolId != 0)
            {
                logger?.Error("Invalid protocol ID: {protocolId}", protocolId);
                return Rx<byte[]>.Fail($"Invalid protocol ID: {protocolId}", response);
            }

            // 验证长度
            if (length != response.Length - 6)
            {
                logger?.Error("Length mismatch: expected {expectedLength}, actual {actualLength}", length, response.Length - 6);
                return Rx<byte[]>.Fail($"Length mismatch: expected {length}, actual {response.Length - 6}", response);
            }

            // 提取 payload（去除 MBAP 头）
            byte[] payload = new byte[response.Length - 6];
            Array.Copy(response, 6, payload, 0, payload.Length);

            return Rx<byte[]>.Success(payload);
        }

        /// <summary>
        /// 验证 读取功能的 Rx，对应 Function Code 0x01, 0x02, 0x03, 0x04。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="length">读取的长度。</param>
        /// <param name="protocolType">ModBus 协议类型。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyReadRx(byte[] response, ushort functionCode, ushort length, ModbusProtocolType protocolType)
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

            // 根据协议类型计算期望的响应长度
            int expectedLength;
            if (protocolType == ModbusProtocolType.TCP)
            {
                // TCP 协议：3（从站ID+功能码+字节数） + 数据长度
                expectedLength = 3 + byteCount;
            }
            else
            {
                // RTU 协议：3（从站ID+功能码+字节数） + 数据长度 + 2（CRC校验）
                expectedLength = 3 + byteCount + 2;
            }

            if (response.Length < expectedLength)
            {
                logger?.Error("Invalid response length. Actual {length}, expected {expectedLength}", response.Length, expectedLength);
                return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}, expected {expectedLength}.", response);
            }

            // RTU 协议需要验证 CRC
            if (protocolType == ModbusProtocolType.RTU && !CRC16.ValidateCRC(response))
            {
                logger?.Error("CRC validation failed");
                return Rx<byte[]>.Fail("CRC validation failed", response);
            }

            logger?.Rx("SerialPort", response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 回显 Rx，对应 Function Code 0x05, 0x06。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <param name="protocolType">ModBus 协议类型。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyEchoRx(byte[] response, Tx tx, ModbusProtocolType protocolType)
        {
            if (tx.Data == null)
            {
                logger?.Error("The data is null.");
                return Rx<byte[]>.Fail("The data is null.", response);
            }

            // 根据协议类型计算期望的响应长度
            int expectedLength;
            if (protocolType == ModbusProtocolType.TCP)
            {
                // TCP 协议：2（从站ID+功能码） + 2（地址） + 数据长度
                expectedLength = 2 + 2 + tx.Data.Length;
            }
            else
            {
                // RTU 协议：2（从站ID+功能码） + 2（地址） + 数据长度 + 2（CRC校验）
                expectedLength = 2 + 2 + tx.Data.Length + 2;
            }

            if (response.Length < expectedLength)
            {
                logger?.Error("The request length is not equal to the data length. Actual {length}, expected {expectedLength}", response.Length, expectedLength);   
                return Rx<byte[]>.Fail($"The request length is not equal to the data length. Actual {response.Length}, expected {expectedLength}.", response);
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

            // RTU 协议需要验证 CRC
            if (protocolType == ModbusProtocolType.RTU && !CRC16.ValidateCRC(response))
            {
                logger?.Error("CRC validation failed");
                return Rx<byte[]>.Fail("CRC validation failed", response);
            }

            logger?.Rx("SerialPort", response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 多写入 Rx，对应 Function Code 0x0F, 0x10。
        /// </summary>
        /// <param name="response">响应数据。</param>
        /// <param name="tx">ModBus 请求数据。</param>
        /// <param name="protocolType">ModBus 协议类型。</param>
        /// <returns>验证结果。</returns>
        public static Rx<byte[]> VerifyMultiWriteRx(byte[] response, Tx tx, ModbusProtocolType protocolType)
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

            // 根据协议类型验证响应长度
            int expectedLength;
            if (protocolType == ModbusProtocolType.TCP)
            {
                // TCP 协议：2（从站ID+功能码） + 4（地址+长度）
                expectedLength = 2 + 4;
            }
            else
            {
                // RTU 协议：2（从站ID+功能码） + 4（地址+长度） + 2（CRC校验）
                expectedLength = 2 + 4 + 2;
            }

            if (response.Length < expectedLength)
            {
                logger?.Error("Invalid response length. Actual {length}, expected {expectedLength}", response.Length, expectedLength);
                return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}, expected {expectedLength}.", response);
            }

            // RTU 协议需要验证 CRC
            if (protocolType == ModbusProtocolType.RTU && !CRC16.ValidateCRC(response))
            {
                logger?.Error("CRC validation failed");
                return Rx<byte[]>.Fail("CRC validation failed", response);
            }

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
            return TryExtractRxFrame(buffer, slaveID, functionCode, ModbusProtocolType.RTU, out frame);
        }

        /// <summary>
        /// 尝试提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区。</param>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="protocolType">ModBus 协议类型。</param>
        /// <param name="frame">提取到的报文。</param>
        /// <returns>是否成功提取。</returns>
        public static bool TryExtractRxFrame(List<byte> buffer, byte slaveID, byte functionCode, ModbusProtocolType protocolType, out byte[] frame)
        {
            if (protocolType == ModbusProtocolType.TCP)
            {
                return TryExtractRxFrameTcp(buffer, slaveID, functionCode, out frame);
            }
            else
            {
                return TryExtractRxFrameRtu(buffer, slaveID, functionCode, out frame);
            }
        }

        /// <summary>
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区。</param>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="frame">提取到的报文。</param>
        /// <returns>是否成功提取。</returns>
        private static bool TryExtractRxFrameTcp(List<byte> buffer, byte slaveID, byte functionCode, out byte[] frame)
        {
            frame = [];

            // TCP 报文至少需要 7 字节的 MBAP 头
            if (buffer.Count < 7)
                return false;

            // 解析 MBAP 头
            ushort transactionId = BitExtentions.ToUshort(buffer[1], buffer[0]);
            ushort protocolId = BitExtentions.ToUshort(buffer[3], buffer[2]);
            ushort length = BitExtentions.ToUshort(buffer[5], buffer[4]);
            byte unitId = buffer[6];

            // 验证协议ID（Modbus TCP 协议ID为0）
            if (protocolId != 0)
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, remove first byte and continue.", protocolId);
                buffer.RemoveAt(0);
                return false;
            }

            // 验证从站ID
            if (unitId != slaveID)
            {
                logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove first byte and continue.", unitId, slaveID);
                buffer.RemoveAt(0);
                return false;
            }

            // 计算完整报文长度（MBAP头 + 数据部分）
            int totalLength = 6 + length;
            if (buffer.Count < totalLength)
                return false;

            // 提取完整报文
            var candidate = buffer.Take(totalLength).ToArray();
            logger?.Rx("TCP", candidate);

            // 提取 payload 部分（去除 MBAP 头）
            byte[] payload = new byte[length];
            Array.Copy(candidate, 6, payload, 0, length);

            // 检查功能码
            byte funcCode = payload[1];

            // 异常响应
            if (funcCode == (functionCode | 0x80))
            {
                // 异常响应的 payload 长度应该是 3 字节（从站ID + 功能码 + 异常码）
                if (length != 3)
                {
                    logger?.Warning("Invalid exception response length: {length}, remove first byte and continue.", length);
                    buffer.RemoveAt(0);
                    return false;
                }

                // 移除已处理的报文
                buffer.RemoveRange(0, totalLength);
                frame = payload;
                return true;
            }

            // 正常响应
            if (funcCode == functionCode)
            {
                // 移除已处理的报文
                buffer.RemoveRange(0, totalLength);
                frame = payload;
                return true;
            }

            // 功能码不匹配
            logger?.Warning("Function code mismatch. Actual {funcCode}, expected {functionCode}, remove first byte and continue.", funcCode, functionCode);
            buffer.RemoveAt(0);
            return false;
        }

        /// <summary>
        /// 尝试从 RTU 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区。</param>
        /// <param name="slaveID">从站ID。</param>
        /// <param name="functionCode">功能码。</param>
        /// <param name="frame">提取到的报文。</param>
        /// <returns>是否成功提取。</returns>
        private static bool TryExtractRxFrameRtu(List<byte> buffer, byte slaveID, byte functionCode, out byte[] frame)
        {
            frame = [];

            if (buffer.Count < 5)
                return false;

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
