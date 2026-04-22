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
        public static ModbusResult<ReadOnlyMemory<byte>> ParseRx(ReadOnlyMemory<byte> response, ModbusTx tx)
        {
            if (response.Length == 0)
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is null.");

            if (!ModbusTools.CheckTx(tx))
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("The tx is invalid.");

            bool verifiedResult;

            // 提取帧
            if (tx.ProtocolType == ModbusProtocolType.TCP)
            {
                if (response.Length <= 8)
                {
                    logger?.Error("The reponse is invalid: {@reponse}", response);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The reponse is invalid.");
                }
                verifiedResult = TryExtractTcpRx(response, tx.SlaveId, tx.FunctionCode);
            }
            else
            {
                if (response.Length < ModbusParams.RTU_RESPONSE_MINIMUM_LENGTH)
                {
                    logger?.Error("The reponse is invalid: {@reponse}", response);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The reponse is invalid.");
                }
                verifiedResult = TryExtractRtuRx(response, tx.SlaveId, tx.FunctionCode, out var frame);
                response = verifiedResult ? frame : response;
            }

            if (!verifiedResult)
            {
                logger?.Error("Extract frame failed: {@extractFrame}", response);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("Extract frame failed", response);
            }

            return (byte)tx.FunctionCode switch
            {
                0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(response, tx.FunctionCode, tx.Length, tx.ProtocolType),
                0x05 or 0x06 => VerifyEchoRx(response, tx.SlaveId, tx.FunctionCode, tx.Data, tx.ProtocolType),
                0x0F or 0x10 => VerifyMultiWriteRx(response, tx.SlaveId, tx.FunctionCode, tx.Start, tx.Length, tx.ProtocolType),
                _ => ModbusResult<ReadOnlyMemory<byte>>.Fail("The function code not support.", response),
            };
        }

        /// <summary>
        /// 校验读取功能的报文，对应 Function Code：0x01, 0x02, 0x03, 0x04，该方法仅校验字节计数，基本校验已通过<see cref="TryExtractTcpRx"/>
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="length">读取的长度</param>
        /// <param name="protocolType">ModBus协议类型</param>
        /// <returns>校验结果</returns>
        private static ModbusResult<ReadOnlyMemory<byte>> VerifyReadRx(ReadOnlyMemory<byte> response, ModbusFunctionCode functionCode, ushort length, ModbusProtocolType protocolType)
        {
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort"; // 协议名称，记录log使用
            int startIndex = protocolType == ModbusProtocolType.TCP ? 6 : 0;     // 将 tcp 报文的起始 index 赋值为6,用于和rtu报文校验方式统一逻辑
            byte byteCount = response.Span[startIndex + 2];   // PDU中的字节计数

            // 预计的字节计数
            int expectedPduByteCount;
            if (functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedPduByteCount = length * 2;
            else expectedPduByteCount = (length + 7) / 8;

            if (byteCount != expectedPduByteCount)  // 与实际的字节计数进行比较
            {
                logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedPduByteCount, byteCount);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"Byte count mismatch. Expected {expectedPduByteCount}, actual {byteCount}.", response);
            }

            // if (protocolType == ModbusProtocolType.RTU)
            // {
            //     if (CRC16.ValidateCRC(response))
            //     {

            //     }
            // }

            logger?.Rx(comName, response.Span);
            return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
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
        private static ModbusResult<ReadOnlyMemory<byte>> VerifyEchoRx(ReadOnlyMemory<byte> response, byte slaveId, ModbusFunctionCode functionCode, byte[]? data, ModbusProtocolType protocolType)
        {
            if (data == null)
            {
                logger?.Error("The data is null.");
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("The data is null.", response);
            }

            var tempSpan = response.Span;
            var dataIndex = 4;  // 数据起始位置
            string comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";
            int byteCount = protocolType == ModbusProtocolType.TCP ? response.Length - dataIndex : response.Length - dataIndex - 2;

            if (data.Length != byteCount)
            {
                logger?.Error("The request length is not equal to the data length. Actual {length}, expected {byteCount}", data.Length, byteCount);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The request length is not equal to the data length. Actual {data.Length}, expected {byteCount}.", response);
            }

            if (tempSpan[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, tempSpan[1]);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The function code error : {functionCode}. " + $"The actual function code : {tempSpan[1]}", response);
            }

            for (int i = dataIndex; i < data.Length; i++)
            {
                if (tempSpan[i] != data[i - dataIndex])
                {
                    logger?.Error("The data compared error. Actual {data}, expected {expectedData}", tempSpan[i], data[i - dataIndex]);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The data compared error. Actual {tempSpan[i]}, expected {data[i - dataIndex]}.", response);
                }
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (tempSpan[0] != slaveId || !CRC16.ValidateCRC(tempSpan))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, tempSpan[0]);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The slave id or CRC error : {slaveId}, actual : {tempSpan[0]}.", response);
                }
            }

            logger?.Rx(comName, tempSpan);
            return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
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
        private static ModbusResult<ReadOnlyMemory<byte>> VerifyMultiWriteRx(ReadOnlyMemory<byte> response, byte slaveId, ModbusFunctionCode functionCode, ushort startAddress, ushort length, ModbusProtocolType protocolType)
        {
            var tempSpan = response.Span;
            var start = BitExtentions.ToUshort(tempSpan[3], tempSpan[2]);
            if (start != startAddress)    // 验证起始地址
            {
                logger?.Error("The start address error. Actual {start}, expected {startAddress}", start, startAddress);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The start address error. Actual {start}, expected {startAddress}.", response);
            }

            var byteCount = BitExtentions.ToUshort(tempSpan[5], tempSpan[4]);
            // var frameLength = protocolType == ModbusProtocolType.TCP ? byteCount + 6 : byteCount + 8;
            var comName = protocolType == ModbusProtocolType.TCP ? "TCP" : "SerialPort";

            if (byteCount != length)    // 验证写入的数据长度
            {
                logger?.Error("The length error. Actual {byteCount}, expected {length}", byteCount, length);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The length error. Actual {byteCount}, expected {length}.", response);
            }

            // if (response.Length != frameLength)   // 验证响应长度
            // {
            //     logger?.Error("Invalid response length. Actual {response.Length}, expected {frameLength}", response.Length, frameLength);
            //     return Rx.Fail($"Invalid response length. Actual {response.Length}, expected {frameLength}.", response);
            // }

            if (tempSpan[1] != (ushort)functionCode)
            {
                logger?.Error("The function code error : {functionCode}, and {actualFunctionCode}", functionCode, tempSpan[1]);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The function code error : {functionCode}. " + $"The actual function code : {tempSpan[1]}", response);
            }

            if (protocolType == ModbusProtocolType.RTU)   // RTU 协议需要验证 CRC
            {
                if (tempSpan[0] != slaveId || !CRC16.ValidateCRC(tempSpan))
                {
                    logger?.Error("The slave id or CRC error : {slaveId}, actual {actualSlaveId}", slaveId, tempSpan[0]);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The slave id or CRC error : {slaveId}, actual : {tempSpan[0]}.", response);
                }
            }

            logger?.Rx(comName, tempSpan);
            return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
        }

        /// <summary>
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行基本的校验
        /// 校验项：
        /// 1. 帧长度是否符合要求
        /// 2. 协议ID是否为0x00
        /// 3. 功能码是否与输入一致
        /// 4. 从站ID是否与输入一致（仅记录，不校验）
        /// </summary>
        /// <param name="memory">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <returns>是否成功提取，并通过基本的校验</returns>
        private static bool TryExtractTcpRx(ReadOnlyMemory<byte> memory, byte slaveID, ModbusFunctionCode functionCode)
        {
            var tempSpan = memory.Span;

            // minimum length
            if (tempSpan.Length < 9)
            {
                logger?.Warning("The response is not valid : {@span}", tempSpan.ToArray());
                return false;
            }

            // 计算完整报文长度（MBAP头 + 数据部分）
            ushort length = BitExtentions.ToUshort(tempSpan[5], tempSpan[4]);   // 帧计数
            int totalLength = 6 + length;
            if (tempSpan.Length != totalLength)
            {
                logger?.Warning("Invalid response length. Actual {span.Length}, expected {totalLength}, and span : {@span}", tempSpan.Length, totalLength, tempSpan.ToArray());
                return false;
            }

            // 验证协议ID（ModbusTCP 协议ID为0x0000）
            ushort protocolId = BitExtentions.ToUshort(tempSpan[3], tempSpan[2]);   //协议标识
            if (protocolId != 0x00)
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, and span : {@span}", protocolId, tempSpan.ToArray());
                return false;
            }

            // verify func code
            if (tempSpan[7] >= 0x80 || tempSpan[7] != (byte)functionCode)
            {
                logger?.Warning("Exception Code: {exception code}, and input function code : {function code} ", tempSpan[7], functionCode);
                return false;
            }

            // 验证从站ID
            if (tempSpan[6] != slaveID)
                logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and span : {@span}", tempSpan[6], slaveID, tempSpan.ToArray());

            logger?.Rx("TCP", tempSpan);
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
        private static bool TryExtractRtuRx(ReadOnlyMemory<byte> buffer, byte slaveID, ModbusFunctionCode functionCode, out ReadOnlyMemory<byte> frame)
        {
            frame = buffer;

            if (buffer.Length < 5)
            {
                logger?.Warning("The response is not valid : {@buffer}", buffer.ToArray());
                return false;
            }

            // 5字节为最小帧长度，滑动窗口提取报文，提取失败移除Index 0 
            while (buffer.Length >= 5)
            {
                var id = buffer.Span[0];
                var funcCode = buffer.Span[1];

                if (id != slaveID)
                {
                    logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.", id, slaveID);
                    buffer = buffer[1..];
                    continue;
                }

                if (funcCode != (byte)functionCode)
                {
                    // 异常响应
                    if (funcCode == ((byte)functionCode | 0x80))
                    {
                        var candidate = buffer[..5];
                        if (CRC16.ValidateCRC(candidate.Span))
                        {
                            logger?.Rx("SerialPort", candidate.Span);
                            frame = candidate;
                            return true;
                        }
                    }
                    logger?.Warning("The actual function code is not matched. Actual {actualFuncCode}, expected {expectedFuncCode}, remove it and continue.", funcCode, functionCode);
                    buffer = buffer[1..];
                    continue;
                }

                // Read
                if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
                {
                    // 校验最低帧长度
                    if (VerifyReadRx(buffer.Span, functionCode, 0, ModbusProtocolType.RTU))
                    {
                        logger?.Information("The response is not valid : {@buffer}", buffer.ToArray());
                        return false;
                    }
                    // var frameLength = 3 + buffer.Span[2] + 2;
                    // if (frameLength > buffer.Length)
                    // {
                    //     logger?.Warning("The frame length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}", buffer.Length, frameLength, buffer.ToArray());
                    //     return false;
                    // }

                    // var candidate = buffer[..frameLength];
                    // if (CRC16.ValidateCRC(candidate.Span))
                    // {
                    //     logger?.Rx("SerialPort", candidate.Span);
                    //     frame = candidate;
                    //     return true;
                    // }

                    // logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    // buffer = buffer[1..];
                    continue;
                }

                //Write
                if (functionCode >= ModbusFunctionCode.WriteCoil && functionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
                {
                    var expectedLength = 8; // 最低长度
                    if (expectedLength > buffer.Length)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer.ToArray());
                        return false;
                    }

                    var candidate = buffer[..expectedLength];
                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        frame = candidate;
                        return true;
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    buffer = buffer[1..];
                    continue;
                }

                /* 
                 *  待添加其他功能码
                 *  例如：
                 *      0x06， 
                 *      0x0F， 
                 *      0x10， 
                 *      0x11等....
                */

                // 当ID匹配，但是功能码不匹配时，其实这部分还能有点补充，例如 0x07， 0x08， 0x14， 0x15等
                logger?.Warning("Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.", buffer.ToArray());
                buffer = buffer[1..];
                continue;
            }

            logger?.Error("The Rx match failed {@buffer}", buffer.ToArray());
            return false;
        }
    }
}
