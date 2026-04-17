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
        /// <param name="response">ModBus 响应数据</param>
        /// <param name="tx">ModBus 请求数据</param>
        /// <returns>解析后的响应数据</returns>
        public static Rx<byte[]> ParseRx(byte[] response, Tx tx)
        {
            if (response == null)
                return Rx<byte[]>.Fail("The response is null.");

            bool extractResult;
            byte[] extractFrame;
            byte[] pdu = [];

            if (tx.ProtocolType == ModbusProtocolType.TCP)
            {
                extractResult = TryExtractTcpRx(response, tx.SlaveId, tx.FunctionCode, out extractFrame);
                pdu = (byte[])extractFrame.Clone();
                extractFrame = extractFrame.Skip(6).ToArray();
            }
            else
                extractResult = TryExtractRtuRx(response.ToList(), tx.SlaveId, tx.FunctionCode, out extractFrame);

            if (!extractResult)
            {
                logger?.Error("Extract frame failed: {@extractFrame}", extractFrame);
                return Rx<byte[]>.Fail("Extract frame failed", extractFrame);
            }

            var result = (byte)tx.FunctionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(extractFrame, tx.SlaveId, tx.FunctionCode, tx.Length, tx.ProtocolType),
                0x05 or 0x06 => VerifyEchoRx(extractFrame, tx.SlaveId, tx.FunctionCode, tx.Data, tx.ProtocolType),
                0x0F or 0x10 => VerifyMultiWriteRx(extractFrame, tx.SlaveId, tx.FunctionCode, tx.Start, tx.Length, tx.ProtocolType),
                _ => Rx<byte[]>.Fail("The function code not support.", response),
            };

            if (result.IsSuccess)
            {
                result.Data = pdu;
                return result;
            }
            else return result;
        }

        /// <summary>
        /// 自动检测 ModBus 协议类型
        /// </summary>
        /// <param name="response">ModBus 响应数据</param>
        /// <returns>检测到的协议类型</returns>
        public static ModbusProtocolType DetectProtocolType(byte[] response)
        {
            if (response != null && response.Length >= 7)
            {
                if (response[2] == 0x00 && response[3] == 0x00)
                {
                    var uLength = BitExtentions.ToUshort(response[5], response[4]);

                    if (uLength == response.Length)
                        return ModbusProtocolType.TCP;
                }
            }
            return ModbusProtocolType.RTU;
        }

        /// <summary>
        /// 验证读取功能的报文，对应 Function Code 0x01, 0x02, 0x03, 0x04
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveId">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="length">读取的长度</param>
        /// <param name="protocolType">ModBus 协议类型</param>
        /// <returns>验证结果</returns>
        private static Rx<byte[]> VerifyReadRx(byte[] response, byte slaveId, ModBusFunctionCode functionCode, ushort length, ModbusProtocolType protocolType)
        {
            int expectedByteCount;  // 根据功能码预计的数据长度，用于创建数组存储数据
            byte byteCount = response[2];   // 字节计数
            int expectedLength = protocolType == ModbusProtocolType.TCP ? 3 + byteCount : 3 + byteCount + 2;    // 根据字节计数计算的帧长度
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort"; // 协议名称，记录log使用

            if (functionCode == ModBusFunctionCode.ReadHodingRegister || functionCode == ModBusFunctionCode.ReadInputRegister)
                expectedByteCount = length * 2;
            else expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
            {
                logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
                return Rx<byte[]>.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.", response);
            }

            if (response.Length != expectedLength)
            {
                logger?.Error("Invalid response length. Actual {length}, expected {expectedLength}", response.Length, expectedLength);
                return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}, expected {expectedLength}.", response);
            }

            // RTU 协议需要验证 CRC
            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Rx<byte[]>.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证回显报文，对应 Function Code 0x05, 0x06
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveId">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="length">读取的长度</param>
        /// <param name="data">ModBus 请求数据</param>
        /// <param name="protocolType">ModBus 协议类型</param>
        /// <returns>验证结果</returns>
        private static Rx<byte[]> VerifyEchoRx(byte[] response, byte slaveId, ModBusFunctionCode functionCode, byte[]? data, ModbusProtocolType protocolType)
        {
            if (data == null)
            {
                logger?.Error("The data is null.");
                return Rx<byte[]>.Fail("The data is null.", response);
            }

            var dataIndex = 4;  // 数据起始位置
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";
            int byteCount = protocolType == ModbusProtocolType.TCP ? response.Length - dataIndex : response.Length - dataIndex - 2;

            if (data.Length != byteCount)
            {
                logger?.Error("The request length is not equal to the data length. Actual {length}, expected {byteCount}", data.Length, byteCount);
                return Rx<byte[]>.Fail($"The request length is not equal to the data length. Actual {data.Length}, expected {byteCount}.", response);
            }

            if (response[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, response[1]);
                return Rx<byte[]>.Fail($"The function code error : {functionCode}. " + $"The actual function code : {response[1]}", response);
            }

            for (int i = dataIndex; i < data.Length; i++)
            {
                if (response[i] != data[i - dataIndex])
                {
                    logger?.Error("The data compared error. Actual {data}, expected {expectedData}", response[i], data[i - dataIndex]);
                    return Rx<byte[]>.Fail($"The data compared error. Actual {response[i]}, expected {data[i - dataIndex]}.", response);
                }
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Rx<byte[]>.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 验证 多写入 Rx，对应 Function Code 0x0F, 0x10
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveId">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="startAddress">起始地址</param>
        /// <param name="length">写入的数据长度</param>
        /// <param name="protocolType">ModBus 协议类型</param>
        /// <returns>验证结果</returns>
        private static Rx<byte[]> VerifyMultiWriteRx(byte[] response, byte slaveId, ModBusFunctionCode functionCode, ushort startAddress, ushort length, ModbusProtocolType protocolType)
        {
            var start = BitExtentions.ToUshort(response[3], response[2]);
            if (start != startAddress)    // 验证起始地址
            {
                logger?.Error("The start address error. Actual {start}, expected {startAddress}", start, startAddress);
                return Rx<byte[]>.Fail($"The start address error. Actual {start}, expected {startAddress}.", response);
            }

            var byteCount = BitExtentions.ToUshort(response[5], response[4]);
            // var frameLength = protocolType == ModbusProtocolType.TCP ? byteCount + 6 : byteCount + 8;
            var comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";

            if (byteCount != length)    // 验证写入的数据长度
            {
                logger?.Error("The length error. Actual {byteCount}, expected {length}", byteCount, length);
                return Rx<byte[]>.Fail($"The length error. Actual {byteCount}, expected {length}.", response);
            }

            // if (response.Length != frameLength)   // 验证响应长度
            // {
            //     logger?.Error("Invalid response length. Actual {response.Length}, expected {frameLength}", response.Length, frameLength);
            //     return Rx<byte[]>.Fail($"Invalid response length. Actual {response.Length}, expected {frameLength}.", response);
            // }

            if (response[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, response[1]);
                return Rx<byte[]>.Fail($"The function code error : {functionCode}. " + $"The actual function code : {response[1]}", response);
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Rx<byte[]>.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Rx<byte[]>.Success(response);
        }

        /// <summary>
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="frame">提取到的报文（去除 MBAP 头）</param>
        /// <returns>是否成功提取</returns>
        private static bool TryExtractTcpRx(byte[] buffer, byte slaveID, ModBusFunctionCode functionCode, out byte[] frame)
        {
            frame = buffer;

            if (buffer.Length < 8)
                return false;

            // 解析 MBAP 头
            ushort protocolId = BitExtentions.ToUshort(buffer[3], buffer[2]);   //协议标识
            ushort length = BitExtentions.ToUshort(buffer[5], buffer[4]);   // 字节计数
            byte unitId = buffer[6];   // 从站ID

            if (protocolId != 0x0000)   // 验证协议ID（ModbusTCP 协议ID为0x0000）
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, remove first byte and continue.", protocolId);
                return false;
            }

            if (unitId != slaveID)  // 验证从站ID
            {
                logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove first byte and continue.", unitId, slaveID);
                // return false;
            }

            // 计算完整报文长度（MBAP头 + 数据部分）
            int totalLength = 6 + length;
            if (buffer.Length != totalLength)
                return false;

            var candidate = buffer.Take(totalLength).ToArray(); // 提取完整报文
            logger?.Rx("TCP", candidate);

            byte funcCode = candidate[7];
            if (funcCode == ((byte)functionCode | 0x80))  // 异常响应
            {
                if (length != 3)  // 异常响应的长度应该是 3 字节（从站ID + 功能码 + 异常码）
                {
                    logger?.Warning("Invalid exception response length: {length}, remove first byte and continue.", length);
                    return false;
                }

                frame = candidate;
                return true;
            }

            if (funcCode == (byte)functionCode)  // 正常响应
            {
                frame = candidate;
                return true;
            }

            // 功能码不匹配
            logger?.Warning("Function code mismatch. Actual {funcCode}, expected {functionCode}, remove first byte and continue.", funcCode, functionCode);
            return false;
        }

        /// <summary>
        /// 尝试从 RTU 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="frame">提取到的报文</param>
        /// <returns>是否成功提取</returns>
        private static bool TryExtractRtuRx(List<byte> buffer, byte slaveID, ModBusFunctionCode functionCode, out byte[] frame)
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
                if (funcCode == ((byte)functionCode | 0x80))
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
                if (id == slaveID && (byte)functionCode == funcCode
                    && (functionCode == ModBusFunctionCode.ReadCoils
                    || functionCode == ModBusFunctionCode.ReadDiscreteInputs
                    || functionCode == ModBusFunctionCode.ReadHodingRegister || functionCode == ModBusFunctionCode.ReadInputRegister))
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
                if (id == slaveID && (byte)functionCode == funcCode
                    && (functionCode == ModBusFunctionCode.WriteCoils
                    || functionCode == ModBusFunctionCode.WriteMultiCoils
                    || functionCode == ModBusFunctionCode.WriteHodingRegister
                    || functionCode == ModBusFunctionCode.WriteMultiHodingRegister))
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
