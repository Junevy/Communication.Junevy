using System.Runtime.InteropServices;
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

            if (ModbusTools.CheckTx(tx))
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("The tx is invalid.");

            // var test = response.Span;
            ModbusResult<ReadOnlyMemory<byte>> verifiedResult;

            // 提取帧
            if (tx.ProtocolType == ModbusProtocolType.TCP)
                verifiedResult = TryExtractTcpRx(response, tx.SlaveId, tx.FunctionCode);
            else
            {
                verifiedResult = TryExtractRtuRx(response, tx.SlaveId, tx.Length, tx.FunctionCode);
                // response = verifiedResult ? frame : response;
            }

            if (!verifiedResult.IsSuccess)
            {
                logger?.Error("Extract frame failed: {@extractFrame}", response);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("Extract frame failed", response);
            }

            // return (byte)tx.FunctionCode switch
            // {
            //     0x01 or 0x02 or 0x03 or 0x04 => VerifyReadRx(response, tx.SlaveId, tx.FunctionCode, tx.Length, tx.ProtocolType),
            //     0x05 or 0x06 => VerifyEchoRx(response, tx.SlaveId, tx.FunctionCode, tx.Data, tx.ProtocolType),
            //     0x0F or 0x10 => VerifyMultiWriteRx(response, tx.SlaveId, tx.FunctionCode, tx.Start, tx.Length, tx.ProtocolType),
            //     _ => ModbusResult<ReadOnlyMemory<byte>>.Fail("The function code not support.", response),
            // };
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
        private static bool VerifyReadRx(ReadOnlySpan<byte> response, ModbusFunctionCode functionCode, ushort length)
        {
            int expectedByteCount;  // 根据功能码预计的数据长度
            byte byteCount = response[2];   // 字节计数

            if (functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedByteCount = length * 2;
            else expectedByteCount = (length + 7) / 8;

            if (byteCount == expectedByteCount)
                return true;

            logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
            return false;
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
        private static bool VerifyEchoRx(ReadOnlySpan<byte> response, ushort startAddress, ushort length, ModbusFunctionCode functionCode, byte[] data, ModbusProtocolType protocolType, out int frameCutLength)
        {
            // var dataIndex = 4;  // 数据起始位置
            frameCutLength = 0;

            var dataLength = functionCode == ModbusFunctionCode.WriteCoil ? (length % 8) : (length * 2);
            if (data.Length != dataLength)
            {
                logger?.Warning("The data length is not valid : {@buffer}", response.ToArray());
                return false;
            }

            var expectedFrameLength = frameCutLength = protocolType == ModbusProtocolType.RTU ? 6 + dataLength + 2 : 6 + dataLength;
            if (response.Length < expectedFrameLength)
            {
                logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                return false;
            }

            var startAdr = BitExtentions.ToUshort(response[3], response[2]);
            if (startAdr != startAddress)
            {
                logger?.Error("The start address error : {startAddress}, and {actualStartAddress}", startAddress, startAdr);
                return false;
            }

            var frameSpan = response.Slice(6, dataLength);
            return frameSpan.SequenceEqual(data);
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
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="buffer">缓存区</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="frame">提取到的报文（去除 MBAP 头）</param>
        /// <returns>是否成功提取</returns>
        private static bool TryExtractTcpRx(ReadOnlyMemory<byte> memory, byte slaveID, ModbusFunctionCode functionCode)
        {
            var tempSpan = memory.Span;

            if (tempSpan.Length < ModbusParams.TCP_RESPONSE_MINIMUM_LENGTH)
            {
                logger?.Warning("The response is not valid : {@span}", tempSpan.ToArray());
                return false;
            }

            // 解析 MBAP 头
            ushort protocolId = BitExtentions.ToUshort(tempSpan[3], tempSpan[2]);   //协议标识
            ushort length = BitExtentions.ToUshort(tempSpan[5], tempSpan[4]);   // 字节计数
            byte unitId = tempSpan[ModbusParams.MBAP_LENGTH - 1];   // 从站ID

            if (protocolId != ModbusParams.TCP_PROTOCOL_ID)   // 验证协议ID（ModbusTCP 协议ID为0x0000）
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, and span : {@span}", protocolId, tempSpan.ToArray());
                return false;
            }

            if (unitId != slaveID)  // 验证从站ID
                logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and span : {@span}", unitId, slaveID, tempSpan.ToArray());

            // 计算完整报文长度（MBAP头 + 数据部分）
            int totalLength = ModbusParams.MBAP_LENGTH - 1 + length;
            if (memory.Length != totalLength)
            {
                logger?.Warning("Invalid response length. Actual {span.Length}, expected {totalLength}, and span : {@span}", tempSpan.Length, totalLength, tempSpan.ToArray());
                return false;
            }

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
        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractRtuRx(ReadOnlyMemory<byte> buffer, byte slaveID, ushort length, ushort start, ModbusFunctionCode functionCode, byte[] data)
        {
            while (buffer.Length > ModbusParams.RTU_RESPONSE_MINIMUM_LENGTH)
            {
                var id = buffer.Span[0];
                var funcCode = buffer.Span[1];

                if (id != slaveID)
                {
                    logger?.Warning("The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.", id, slaveID);
                    buffer = buffer[1..];
                    continue;
                }

                // 异常响应
                if (funcCode == ((byte)functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (exceptionLength > buffer.Length)
                    {
                        logger?.Error("The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}", buffer.Length, exceptionLength, buffer.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The exception response length is not matched. Actual {buffer.Length}, expected {exceptionLength} and buffer : {@buffer}", buffer);
                    }

                    var candidate = buffer[..exceptionLength];
                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning("The exception response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    buffer = buffer[1..]; // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
                {
                    int byteCount = buffer.Span[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (buffer.Length < expectedLength)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The response is not valid : {@buffer}", buffer);
                    }

                    var candidate = buffer[..expectedLength];
                    var result = VerifyReadRx(candidate.Span, functionCode, length);
                    if (!result)
                    {
                        buffer = buffer[1..];
                        continue;
                    }

                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    buffer = buffer[1..];
                    continue;
                }

                //Single Write
                if (functionCode <= ModbusFunctionCode.WriteCoil && functionCode >= ModbusFunctionCode.WriteHodingRegister)
                {
                    if (data == null || data.Length == 0)
                    {
                        logger?.Warning("The data length is not valid : {@buffer}", buffer.ToArray());
                        buffer = buffer[1..];
                        continue;
                    }

                    var result = VerifyEchoRx(buffer.Span, start, length, functionCode, data, ModbusProtocolType.RTU, out var frameLength);
                    if (!result)
                    {
                        buffer = buffer[1..];
                        continue;
                    }
                    var candidate = buffer[..frameLength];

                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    buffer = buffer[1..];
                    continue;
                }

                // Multi Write

                // 其他功能码，例如 0x06， 0x0F， 0x10， 0x11等
                if (id == slaveID && (byte)functionCode == funcCode)
                {
                    var expectedLength = 6;

                    if (expectedLength > buffer.Length)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", buffer.ToArray());
                        return false;
                    }

                    // var candidate = buffer.Take(expectedLength).ToArray();
                    var candidate = buffer[..expectedLength];
                    logger?.Rx("SerialPort", candidate.Span);

                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        buffer = buffer[expectedLength..];
                        frame = candidate;
                        return true;
                    }

                    logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    buffer = buffer[1..];
                    continue;
                }

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
