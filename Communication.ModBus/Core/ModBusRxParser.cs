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

            ModbusResult<ReadOnlyMemory<byte>> verifiedResult;

            // 提取帧
            if (tx.ProtocolType == ModbusProtocolType.TCP)
                verifiedResult = TryExtractTcpRx(response, tx.SlaveId, tx.Length, tx.Start, tx.FunctionCode,
                    tx.Data ?? []);
            else
                verifiedResult = TryExtractRtuRx(response, tx.SlaveId, tx.Length, tx.Start, tx.FunctionCode,
                    tx.Data ?? []);

            if (verifiedResult.IsSuccess) return verifiedResult;

            logger?.Error("Extract frame failed: {@extractFrame}", response);
            return ModbusResult<ReadOnlyMemory<byte>>.Fail("Extract frame failed", response);
        }

        /// <summary>
        /// 验证读取功能的报文，对应 Function Code 0x01, 0x02, 0x03, 0x04
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="length">读取长度</param>
        /// <returns>验证结果</returns>
        private static bool VerifyReadRx(ReadOnlySpan<byte> response, ModbusFunctionCode functionCode, ushort length)
        {
            int expectedByteCount; // 根据功能码预计的数据长度
            byte byteCount = response[2]; // 字节计数

            if (functionCode == ModbusFunctionCode.ReadHodingRegisters ||
                functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedByteCount = length * 2;
            else expectedByteCount = (length + 7) / 8;

            if (byteCount == expectedByteCount)
                return true;

            logger?.Error("Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount,
                byteCount);
            return false;
        }

        /// <summary>
        /// 验证回显报文，对应 Function Code 0x05, 0x06
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="startAddress">起始地址</param>
        /// <param name="data">ModBus 请求数据</param>
        /// <returns>验证结果</returns>
        private static bool VerifyEchoRx(ReadOnlySpan<byte> response, ushort startAddress, byte[] data)
        {
            var startAdr = BitExtentions.ToUshort(response[3], response[2]);
            if (startAdr != startAddress)
            {
                logger?.Error("The start address error : {startAddress}, and {actualStartAddress}", startAddress,
                    startAdr);
                return false;
            }

            if (data.Length != 2)
            {
                logger?.Error("The data length is invalid!{dataLength}", data.Length);
                return false;
            }

            var frameSpan = response.Slice(4, 2);
            return frameSpan.SequenceEqual(data);
        }

        /// <summary>
        /// 验证 多写入功能的报文，对应 Function Code 0x0F, 0x10
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="startAddress">起始地址</param>
        /// <param name="length">写入长度</param>
        /// <returns>验证结果</returns>
        private static bool VerifyMultiWriteRx(ReadOnlySpan<byte> response, ushort startAddress, ushort length)
        {
            var startAdr = BitExtentions.ToUshort(response[3], response[2]);
            if (startAdr != startAddress) // 验证起始地址
            {
                logger?.Error("The start address error. Actual {start}, expected {startAddress}", startAdr,
                    startAddress);
                return false;
            }

            var dataLength = BitExtentions.ToUshort(response[5], response[4]);
            if (dataLength != length) // 验证写入的数据长度
            {
                logger?.Error("The length error. Actual {dataLength}, expected {length}", dataLength, length);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 尝试从 TCP 流中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveID">从站ID</param>
        /// <param name="length">写入长度</param>
        /// <param name="starAddr">起始地址</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="data">ModBus 请求数据</param>
        /// <returns>是否成功提取</returns>
        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractTcpRx(ReadOnlyMemory<byte> response, byte slaveID,
            ushort length, ushort starAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            if (response.Length < 9) // Minimum length
            {
                logger?.Warning("The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response);
            }

            // 解析 MBAP 头
            ushort protocolId = BitExtentions.ToUshort(response.Span[3], response.Span[2]); //协议标识
            ushort frameLength = BitExtentions.ToUshort(response.Span[5], response.Span[4]); // 帧长度
            byte funcCode = response.Span[7];
            byte unitId = response.Span[6]; // 从站ID

            if (protocolId != 0x0000) // 验证协议ID（ModbusTCP 协议ID为0x0000）
            {
                logger?.Warning("Invalid protocol ID: {protocolId}, and span : {@span}", protocolId,
                    response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"Invalid protocol ID: {protocolId}.", response);
            }

            if ((funcCode & 0x80) != 0)
            {
                const int exceptionLength = 6 + 3;

                if (response.Length != exceptionLength)
                {
                    logger?.Error(
                        "The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}",
                        response.Length, exceptionLength, response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(
                        $"The exception response length is not matched. Actual {response.Length}, expected {exceptionLength} and buffer : {response}",
                        response);
                }
                logger?.Error("Exception Code: {funcCode}", funcCode);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"Exception Code: {funcCode}");
            }

            if (unitId != slaveID) // 验证从站ID
                logger?.Warning(
                    "The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and span : {@span}",
                    unitId, slaveID, response.ToArray());

            int totalLength = 6 + frameLength;
            if (response.Length < totalLength) // 计算完整报文长度（MBAP头 + 数据部分）
            {
                logger?.Warning(
                    "Invalid response length. Actual {span.Length}, expected {totalLength}, and span : {@span}",
                    response.Length, totalLength, response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    $"Invalid response length. Actual {response.Length}, expected {totalLength}.", response);
            }

            switch (functionCode)
            {
                // Read
                case >= ModbusFunctionCode.ReadCoils and <= ModbusFunctionCode.ReadInputRegisters:
                {
                    int byteCount = response.Span[8];
                    var expectedLength = 6 + 3 + byteCount;

                    if (response.Length < expectedLength) // 验证响应长度
                    {
                        logger?.Warning("The response is not valid : {@span}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response);
                    }

                    var cutFrame = response[..expectedLength];
                    var result = VerifyReadRx(cutFrame.Span.Slice(6, 3 + byteCount), functionCode, length);
                    if (result) return ModbusResult<ReadOnlyMemory<byte>>.Success(cutFrame);

                    logger?.Warning("The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response);
                }

                // Write single
                case ModbusFunctionCode.WriteCoil:
                case ModbusFunctionCode.WriteHodingRegister:
                {
                    if (data == null || data.Length == 0 || response.Length < 12)
                    {
                        logger?.Warning("The data length is not valid : {@span}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail("The data length is not valid", response);
                    }

                    var result = VerifyEchoRx(response.Span[6..12], starAddr, data);
                    if (result) return ModbusResult<ReadOnlyMemory<byte>>.Success(response);

                    logger?.Warning("The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response);
                }

                // Multi Write
                case ModbusFunctionCode.WriteMultiCoils:
                case ModbusFunctionCode.WriteMultiHodingRegisters:
                {
                    var result = VerifyMultiWriteRx(response.Span.Slice(6, 3 + length), starAddr, length);
                    if (result) return ModbusResult<ReadOnlyMemory<byte>>.Success(response);

                    logger?.Warning("The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response);
                }

                default:
                    logger?.Rx("TCP", response.Span);
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail("The function code cannot be matched.", response);
            }
        }

        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractRtuRx(ReadOnlyMemory<byte> response, byte slaveID,
            ushort length, ushort startAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            while (response.Length > 5)
            {
                var id = response.Span[0];
                var funcCode = response.Span[1];

                if (id != slaveID)
                {
                    logger?.Warning(
                        "The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.",
                        id, slaveID);
                    response = response[1..];
                    continue;
                }

                // 异常响应
                if (funcCode == ((byte)functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (exceptionLength > response.Length)
                    {
                        logger?.Error(
                            "The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}",
                            response.Length, exceptionLength, response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail(
                            $"The exception response length is not matched. Actual {response.Length}, expected {exceptionLength} and buffer : {response}",
                            response);
                    }

                    var candidate = response[..exceptionLength];
                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(
                        "The exception response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.",
                        candidate.Span[4], candidate.Span[3]);
                    response = response[1..]; // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if (functionCode >= ModbusFunctionCode.ReadCoils &&
                    functionCode <= ModbusFunctionCode.ReadInputRegisters)
                {
                    int byteCount = response.Span[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (response.Length < expectedLength)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The response is not valid : {response}",
                            response);
                    }

                    var candidate = response[..expectedLength];
                    var result = VerifyReadRx(candidate.Span, functionCode, length);
                    if (!result)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                        response = response[1..];
                        continue;
                    }

                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(
                        "The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.",
                        candidate.Span[4], candidate.Span[3]);
                    response = response[1..];
                    continue;
                }

                //Single Write
                else if (functionCode <= ModbusFunctionCode.WriteCoil &&
                         functionCode >= ModbusFunctionCode.WriteHodingRegister)
                {
                    if (data == null || data.Length == 0 || response.Length < 8)
                    {
                        logger?.Warning("The data length is not valid : {@buffer}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail("The data length is not valid", response);
                    }

                    var result = VerifyEchoRx(response.Span[..8], startAddr, data);
                    if (!result)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                        response = response[1..];
                        continue;
                    }

                    var candidate = response[..8];
                    if (CRC16.ValidateCRC(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(
                        "The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.",
                        candidate.Span[4], candidate.Span[3]);
                    response = response[1..];
                    continue;
                }

                // Multi Write
                else if (functionCode >= ModbusFunctionCode.WriteMultiCoils &&
                         functionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
                {
                    var result = VerifyMultiWriteRx(response.Span[..8], startAddr, length);
                    if (!result)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                        response = response[1..];
                        continue;
                    }

                    return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
                }
                /*
                // 其他功能码，例如 0x06， 0x0F， 0x10， 0x11等
                // if (id == slaveID && (byte)functionCode == funcCode)
                // {
                //     var expectedLength = 6;

                //     if (expectedLength > buffer.Length)
                //     {
                //         logger?.Warning("The response is not valid : {@buffer}", buffer.ToArray());
                //         return false;
                //     }

                //     // var candidate = buffer.Take(expectedLength).ToArray();
                //     var candidate = buffer[..expectedLength];
                //     logger?.Rx("SerialPort", candidate.Span);

                //     if (CRC16.ValidateCRC(candidate.Span))
                //     {
                //         buffer = buffer[expectedLength..];
                //         frame = candidate;
                //         return true;
                //     }

                //     logger?.Warning("The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                //     buffer = buffer[1..];
                //     continue;
                // }
                */

                // 当ID匹配，但是功能码不匹配时，这部分还能有点补充，例如 0x07， 0x08， 0x14， 0x15等
                logger?.Warning(
                    "Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.",
                    response.ToArray());
                response = response[1..];
                continue;
            }

            logger?.Error("The Rx match failed {@buffer}", response.ToArray());
            return ModbusResult<ReadOnlyMemory<byte>>.Fail("The Rx match failed", response);
        }
    }
}