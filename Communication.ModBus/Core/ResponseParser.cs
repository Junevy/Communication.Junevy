using Communication.Modbus.Common;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;

namespace Communication.Modbus.Core
{
    public static class ResponseParser
    {
        private static readonly ISerilog? logger = Serilogger.Instance;

        public static ModbusResult<ReadOnlyMemory<byte>> ParseResponse(ReadOnlyMemory<byte> response, ModbusRequest request)
        {
            if (response.Length == 0)
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [ParseResponse] The response is null.");

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [ParseResponse] The request is invalid.");

            ModbusResult<ReadOnlyMemory<byte>> verifiedResult;

            // 提取帧
            if (request.ProtocolType == ModbusProtocolType.TCP)
                verifiedResult = TryExtractTcpResponse(response, request.SlaveId, request.Length, request.Start, request.FunctionCode, request.Data ?? []);
            else
                verifiedResult = TryExtractRtuResponse(response, request.SlaveId, request.Length, request.Start, request.FunctionCode, request.Data ?? []);

            if (!verifiedResult.IsSuccess)
            {
                logger?.Error(" [ParseResponse] Extract frame failed: {@extractFrame}", response);
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [ParseResponse] Extract frame failed", response);
            }

            return verifiedResult;
        }

        /// <summary>
        /// 验证Modbus的读取的回复报文，对应 Function Code: 0x01, 0x02, 0x03, 0x04
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="functionCode">功能码</param>
        /// <param name="length">读取的长度</param>
        /// <returns>验证结果</returns>
        private static bool VerifyRead(ReadOnlySpan<byte> response, ModbusFunctionCode functionCode, ushort length)
        {
            int expectedByteCount;  // 根据功能码预计的数据长度
            byte byteCount = response[2];   // 字节计数

            if (functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedByteCount = length * 2;
            else expectedByteCount = (length + 7) / 8;

            if (byteCount == expectedByteCount)
                return true;
            logger?.Error(" [VerifyReadRx] Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
            return false;
        }

        /// <summary>
        /// 验证Modbus的单写入的回复报文，对应 Function Code: 0x05, 0x06
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="startAddress">起始地址</param>
        /// <param name="data">ModBus 请求数据</param>
        /// <returns>验证结果</returns>
        private static bool VerifySingleWrite(ReadOnlySpan<byte> response, ushort startAddress, byte[] data)
        {
            var startAdr = BinaryExtensions.ToUshort(response[3], response[2]);
            if (startAdr != startAddress)
            {
                logger?.Error(" [VerifyEchoRx] The start address error : {startAddress}, and {actualStartAddress}", startAddress, startAdr);
                return false;
            }

            if (data.Length != 2)
            {
                logger?.Error(" [VerifyEchoRx] The data length is invalid!{dataLength}", data.Length);
                return false;
            }

            var frameSpan = response.Slice(4, 2);
            return frameSpan.SequenceEqual(data);
        }

        /// <summary>
        /// 验证Modbus的多写入的回复报文，对应 Function Code: 0x0F, 0x10
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="startAddress">起始地址</param>
        /// <param name="length">写入的数据长度</param>
        /// <returns>验证结果</returns>
        private static bool VerifyMultiWrite(ReadOnlySpan<byte> response, ushort startAddress, ushort length)
        {
            var start = BinaryExtensions.ToUshort(response[3], response[2]);
            if (start != startAddress)    // 验证起始地址
            {
                logger?.Error(" [VerifyMultiWriteRx] The start address error. Actual {start}, expected {startAddress}", start, startAddress);
                return false;
            }

            var dataLength = BinaryExtensions.ToUshort(response[5], response[4]);
            if (dataLength != length)    // 验证写入的数据长度
            {
                logger?.Error(" [VerifyMultiWriteRx] The length error. Actual {dataLength}, expected {length}", dataLength, length);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 尝试从 TCP 报文中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveID">请求从站ID</param>
        /// <param name="length">请求数据长度</param>
        /// <param name="starAddr">请求起始地址</param>
        /// <param name="functionCode">请求功能码</param>
        /// <param name="data">请求数据</param>
        /// <returns>是否成功提取</returns>
        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractTcpResponse(ReadOnlyMemory<byte> response, byte slaveID, ushort length, ushort startAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            if (response.Length < 9)    // Minimum length
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            // 解析 MBAP 头
            ushort protocolId = BinaryExtensions.ToUshort(response.Span[3], response.Span[2]);   //协议标识
            ushort frameLength = BinaryExtensions.ToUshort(response.Span[5], response.Span[4]);   // 帧长度
            byte unitId = response.Span[6];   // 从站ID

            if (protocolId != 0x0000)   // 验证协议ID（ModbusTCP 协议ID为0x0000）
            {
                logger?.Warning(" [TryExtractTcpRx] Invalid protocol ID: {protocolId}, and span : {@span}", protocolId, response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"Invalid protocol ID: {protocolId}.", response);
            }

            if (unitId != slaveID)  // 验证从站ID
                logger?.Warning(" [TryExtractTcpRx] The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and span : {@span}", unitId, slaveID, response.ToArray());

            int totalLength = 6 + frameLength;
            if (response.Length < totalLength)  // 计算完整报文长度（MBAP头 + 数据部分）
            {
                logger?.Warning(" [TryExtractTcpRx] Invalid response length. Actual {span.Length}, expected {totalLength}, and span : {@span}", response.Length, totalLength, response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($" [TryExtractTcpRx] Invalid response length. Actual {response.Length}, expected {totalLength}.", response);
            }

            // Read
            if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
            {
                int byteCount = response.Span[8];
                var expectedLength = 6 + 3 + byteCount;

                if (response.Length < expectedLength)  // 验证响应长度
                {
                    logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
                }

                var cutFrame = response[..expectedLength];
                var result = VerifyRead(cutFrame.Span.Slice(6, 3 + byteCount), functionCode, length);
                if (!result)
                {
                    logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
                }
                return ModbusResult<ReadOnlyMemory<byte>>.Success(cutFrame);
            }
            // Write single
            else if (functionCode >= ModbusFunctionCode.WriteCoil && functionCode <= ModbusFunctionCode.WriteHodingRegister)
            {
                if (data == null || data.Length == 0 || response.Length < 12)
                {
                    logger?.Warning(" [TryExtractTcpRx] The data length is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The data length is not valid", response);
                }

                var result = VerifySingleWrite(response.Span[6..12], startAddr, data);
                if (!result)
                {
                    logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
                }
                return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
            }
            else if (functionCode >= ModbusFunctionCode.WriteMultiCoils && functionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
            {
                var result = VerifyMultiWrite(response.Span.Slice(6, 3 + length), startAddr, length);
                if (!result)
                {
                    logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                    return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
                }
                return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
            }

            logger?.Rx(" [TryExtractTcpRx] TCP", response.Span);
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The function code cannot be matched.", response);
        }

        /// <summary>
        /// 尝试从 RTU 报文中提取标准格式响应报文，提取后进行校验
        /// </summary>
        /// <param name="response">响应数据</param>
        /// <param name="slaveID">请求从站ID</param>
        /// <param name="length">请求数据长度</param>
        /// <param name="startAddr">请求起始地址</param>
        /// <param name="functionCode">请求功能码</param>
        /// <param name="data">请求数据</param>
        /// <returns>是否成功提取</returns>
        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractRtuResponse(ReadOnlyMemory<byte> response, byte slaveID, ushort length, ushort startAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            while (response.Length > 5)
            {
                var id = response.Span[0];
                var funcCode = response.Span[1];

                if (id != slaveID)
                {
                    logger?.Warning(" [TryExtractRtuRx] The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.", id, slaveID);
                    response = response[1..];
                    continue;
                }

                // 异常响应
                if (funcCode == ((byte)functionCode | 0x80))
                {
                    const int exceptionLength = 5;

                    if (exceptionLength > response.Length)
                    {
                        logger?.Error(" [TryExtractRtuRx] The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}", response.Length, exceptionLength, response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail($"The exception response length is not matched. Actual {response.Length}, expected {exceptionLength} and buffer : {response}", response);
                    }

                    var candidate = response[..exceptionLength];
                    if (Crc16Helper.VerifyCrc(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(" [TryExtractRtuRx] The exception response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    response = response[1..]; // CRC 错，丢弃一个字节继续扫描
                    continue;
                }

                // Read
                if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
                {
                    int byteCount = response.Span[2];
                    var expectedLength = 3 + byteCount + 2;

                    if (response.Length < expectedLength)
                    {
                        logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail($" [TryExtractRtuRx] The response is not valid : {response}", response);
                    }

                    var candidate = response[..expectedLength];
                    var result = VerifyRead(candidate.Span, functionCode, length);
                    if (!result)
                    {
                        logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                        response = response[1..];
                        continue;
                    }

                    if (Crc16Helper.VerifyCrc(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(" [TryExtractRtuRx] The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    response = response[1..];
                    continue;
                }

                //Single Write
                else if (functionCode <= ModbusFunctionCode.WriteCoil && functionCode >= ModbusFunctionCode.WriteHodingRegister)
                {
                    if (data == null || data.Length == 0 || response.Length < 8)
                    {
                        logger?.Warning(" [TryExtractRtuRx] The data length is not valid : {@buffer}", response.ToArray());
                        return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The data length is not valid", response);
                    }

                    var result = VerifySingleWrite(response.Span, startAddr, data);
                    if (!result)
                    {
                        logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
                        response = response[1..];
                        continue;
                    }

                    var candidate = response[..8];
                    if (Crc16Helper.VerifyCrc(candidate.Span))
                    {
                        logger?.Rx("SerialPort", candidate.Span);
                        return ModbusResult<ReadOnlyMemory<byte>>.Success(candidate);
                    }

                    logger?.Warning(" [TryExtractRtuRx] The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
                    response = response[1..];
                    continue;
                }

                // Multi Write
                else if (functionCode >= ModbusFunctionCode.WriteMultiCoils && functionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
                {
                    var result = VerifyMultiWrite(response.Span[..8], startAddr, length);
                    if (!result)
                    {
                        logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
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

                // 当ID匹配，但是功能码不匹配时，其实这部分还能有点补充，例如 0x07， 0x08， 0x14， 0x15等
                logger?.Warning(" [TryExtractRtuRx] Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.", response.ToArray());
                response = response[1..];
                continue;
            }

            logger?.Error(" [TryExtractRtuRx] The Rx match failed {@buffer}", response.ToArray());
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The Rx match failed", response);
        }
    }
}
