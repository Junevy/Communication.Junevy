using Communication.Modbus.Common;
using Communication.Modbus.Utils;

namespace Communication.Modbus.Core
{
    public static class ModbusRxParser
    {
        private static readonly ISerilog? logger = Serilogger.Instance;

        /// <summary>
        /// 解析 ModBus 响应的数据
        /// </summary>
        /// <param name="response">ModBus 响应数据</param>
        /// <param name="tx">ModBus 请求数据</param>
        /// <returns>解析后的响应数据</returns>
        public static Response ParseRx(byte[] response, Request tx)
        {
            if (response == null)
                return Response.Fail("The response is null.");

            bool verifiedResult;
            byte[] frame;
            byte[] pdu;

            // 提取帧
            if (tx.ProtocolType == ModbusProtocolType.TCP)
                verifiedResult = TryExtractTcpRx(response, tx.SlaveId, tx.FunctionCode, out frame);
            else
                verifiedResult = TryExtractRtuRx(response.ToList(), tx.SlaveId, tx.FunctionCode, out frame);

            if (!verifiedResult)
            {
                logger?.Error("Extract frame failed: {@extractFrame}", frame);
                return Response.Fail("Extract frame failed", response);
            }

            // 提取成功,保存提取后数据
            if (tx.ProtocolType == ModbusProtocolType.TCP)
                pdu = frame.Skip(6).ToArray();
            else
                pdu = frame.ToArray();

            var result = (byte)tx.FunctionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(pdu, tx.SlaveId, tx.FunctionCode, tx.Length, tx.ProtocolType),
                0x05 or 0x06 => VerifyEchoRx(pdu, tx.SlaveId, tx.FunctionCode, tx.Data, tx.ProtocolType),
                0x0F or 0x10 => VerifyMultiWriteRx(pdu, tx.SlaveId, tx.FunctionCode, tx.Start, tx.Length, tx.ProtocolType),
                _ => Response.Fail("The function code not support.", frame),
            };
            result.RawData = frame;
            return result;  
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
        private static Response VerifyReadRx(byte[] response, byte slaveId, ModbusFunctionCode functionCode, ushort length, ModbusProtocolType protocolType)
        {
            int expectedByteCount;  // 根据功能码预计的数据长度，用于创建数组存储数据
            byte byteCount = response[2];   // 字节计数
            int expectedLength = protocolType == ModbusProtocolType.TCP ? 3 + byteCount : 3 + byteCount + 2;    // 根据字节计数计算的帧长度
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort"; // 协议名称，记录log使用
            byte functionCodeByte = response[1];

            if (functionCodeByte != (byte)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, actual {actualCode}", functionCode, functionCodeByte);
                return Response.Fail($"The function code error : {functionCode}, actual : {functionCodeByte}.", response);
            }

            if (functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedByteCount = length * 2;
            else expectedByteCount = (length + 7) / 8;

            if (byteCount != expectedByteCount)
            {
                logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
                return Response.Fail($"Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}.", response);
            }

            if (response.Length != expectedLength)
            {
                logger?.Error("Invalid response length. Actual {length}, expected {expectedLength}", response.Length, expectedLength);
                return Response.Fail($"Invalid response length. Actual {response.Length}, expected {expectedLength}.", response);
            }

            // RTU 协议需要验证 CRC
            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Response.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Response.Success(response);
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
        private static Response VerifyEchoRx(byte[] response, byte slaveId, ModbusFunctionCode functionCode, byte[]? data, ModbusProtocolType protocolType)
        {
            if (data == null)
            {
                logger?.Error("The data is null.");
                return Response.Fail("The data is null.", response);
            }

            var dataIndex = 4;  // 数据起始位置
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";
            int byteCount = protocolType == ModbusProtocolType.TCP ? response.Length - dataIndex : response.Length - dataIndex - 2;

            if (data.Length != byteCount)
            {
                logger?.Error("The request length is not equal to the data length. Actual {length}, expected {byteCount}", data.Length, byteCount);
                return Response.Fail($"The request length is not equal to the data length. Actual {data.Length}, expected {byteCount}.", response);
            }

            if (response[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, response[1]);
                return Response.Fail($"The function code error : {functionCode}. " + $"The actual function code : {response[1]}", response);
            }

            for (int i = dataIndex; i < data.Length; i++)
            {
                if (response[i] != data[i - dataIndex])
                {
                    logger?.Error("The data compared error. Actual {data}, expected {expectedData}", response[i], data[i - dataIndex]);
                    return Response.Fail($"The data compared error. Actual {response[i]}, expected {data[i - dataIndex]}.", response);
                }
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Response.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Response.Success(response);
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
        private static Response VerifyMultiWriteRx(byte[] response, byte slaveId, ModbusFunctionCode functionCode, ushort startAddress, ushort length, ModbusProtocolType protocolType)
        {
            var start = BitExtentions.ToUshort(response[3], response[2]);
            if (start != startAddress)    // 验证起始地址
            {
                logger?.Error("The start address error. Actual {start}, expected {startAddress}", start, startAddress);
                return Response.Fail($"The start address error. Actual {start}, expected {startAddress}.", response);
            }

            var byteCount = BitExtentions.ToUshort(response[5], response[4]);
            // var frameLength = protocolType == ModbusProtocolType.TCP ? byteCount + 6 : byteCount + 8;
            var comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";

            if (byteCount != length)    // 验证写入的数据长度
            {
                logger?.Error("The length error. Actual {byteCount}, expected {length}", byteCount, length);
                return Response.Fail($"The length error. Actual {byteCount}, expected {length}.", response);
            }

            // if (response.Length != frameLength)   // 验证响应长度
            // {
            //     logger?.Error("Invalid response length. Actual {response.Length}, expected {frameLength}", response.Length, frameLength);
            //     return Rx.Fail($"Invalid response length. Actual {response.Length}, expected {frameLength}.", response);
            // }

            if (response[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, response[1]);
                return Response.Fail($"The function code error : {functionCode}. " + $"The actual function code : {response[1]}", response);
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (response[0] != slaveId || !CRC16.ValidateCRC(response))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, response[0]);
                    return Response.Fail($"The slave id or CRC error : {slaveId}, actual : {response[0]}.", response);
                }
            }

            logger?.Rx(comName, response);
            return Response.Success(response);
        }

        /// <summary>
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="frame">提取到的报文（去除 MBAP 头）</param>
        /// <returns>是否成功提取</returns>
        private static bool TryExtractTcpRx(byte[] buffer, byte slaveID, ModbusFunctionCode functionCode, out byte[] frame)
        {
            frame = buffer;
            if (buffer.Length < ModbusParams.TCP_RESPONSE_MINIMUM_LENGTH)
            {
                logger?.Warning("The response is not valid : {@buffer}", buffer);
                return false;
            }

            // 解析 MBAP 头
            ushort protocolId = BitExtentions.ToUshort(buffer[3], buffer[2]);   //协议标识
            ushort length = BitExtentions.ToUshort(buffer[5], buffer[4]);   // 字节计数
            byte unitId = buffer[ModbusParams.TCP_DATA_START];   // 从站ID

            if (protocolId != ModbusParams.TCP_PROTOCOL_ID)   // 验证协议ID（ModbusTCP 协议ID为0x0000）
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, and buffer : {@buffer}", protocolId, buffer);
                return false;
            }

            if (unitId != slaveID)  // 验证从站ID
                logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and buffer : {@buffer}", unitId, slaveID, buffer);

            // 计算完整报文长度（MBAP头 + 数据部分）
            int totalLength = ModbusParams.TCP_DATA_START + length;
            if (buffer.Length != totalLength)
            {
                logger?.Warning("Invalid response length. Actual {buffer.Length}, expected {totalLength}, and buffer : {@buffer}", buffer.Length, totalLength, buffer);
                return false;
            }

            var candidate = buffer.Take(totalLength).ToArray(); // 提取完整报文
            logger?.Rx("TCP", candidate);
            frame = candidate;
            return true;
        }

        /// <summary>
        /// 尝试从 RTU 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="frame">提取到的报文</param>
        /// <returns>是否成功提取</returns>
        private static bool TryExtractRtuRx(List<byte> buffer, byte slaveID, ModbusFunctionCode functionCode, out byte[] frame)
        {
            frame = buffer.ToArray();

            if (buffer.Count < ModbusParams.RTU_RESPONSE_MINIMUM_LENGTH)
            {
                logger?.Warning("The response is not valid : {@buffer}", buffer);
                return false;
            }

            while (buffer.Count >= ModbusParams.RTU_RESPONSE_MINIMUM_LENGTH)
            {
                var id = buffer[0];
                var funcCode = buffer[1];

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
                        logger?.Error("The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}", buffer.Count, exceptionLength, buffer);
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
                    && (functionCode == ModbusFunctionCode.ReadCoils
                    || functionCode == ModbusFunctionCode.ReadDiscreteInputs
                    || functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters))
                {
                    int byteCount = buffer[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (buffer.Count < expectedLength)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer);
                        return false;
                    }

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
                    && (functionCode == ModbusFunctionCode.WriteCoil
                    || functionCode == ModbusFunctionCode.WriteMultiCoils
                    || functionCode == ModbusFunctionCode.WriteHodingRegister
                    || functionCode == ModbusFunctionCode.WriteMultiHodingRegisters))
                {
                    var expectedLength = 8;

                    if (expectedLength > buffer.Count)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer);
                        return false;
                    }

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

                // 其他功能码，例如 0x06， 0x0F， 0x10， 0x11等
                if (id == slaveID && (byte)functionCode == funcCode)
                {
                    var expectedLength = 6;

                    if (expectedLength > buffer.Count)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer);
                        return false;
                    }

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

            logger?.Error("The Rx match failed {@buffer}", buffer);
            return false;
        }
    }
}
