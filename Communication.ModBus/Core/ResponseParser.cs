using Communication.Modbus.Common;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;

namespace Communication.Modbus.Core
{
    public static class ResponseParser
    {
        private const int RtuMinFrameLength = 5;
        private const int RtuPduOffset = 0;

        private const int TcpMinFrameLength = 9;
        private const int TcpPduOffset = 6;

        private static readonly ISerilog? logger = Serilogger.Instance;

        private enum FunctionCodeCategory
        {
            Read,
            WriteSingle,
            WriteMulti,
            Unknown
        }

        private static FunctionCodeCategory CategorizeFunctionCode(ModbusFunctionCode functionCode)
        {
            if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
                return FunctionCodeCategory.Read;
            if (functionCode >= ModbusFunctionCode.WriteCoil && functionCode <= ModbusFunctionCode.WriteHodingRegister)
                return FunctionCodeCategory.WriteSingle;
            if (functionCode >= ModbusFunctionCode.WriteMultiCoils && functionCode <= ModbusFunctionCode.WriteMultiHodingRegisters)
                return FunctionCodeCategory.WriteMulti;
            return FunctionCodeCategory.Unknown;
        }

        public static ModbusResult<ReadOnlyMemory<byte>> ParseResponse(ReadOnlyMemory<byte> response, ModbusRequest request)
        {
            if (response.Length == 0)
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [ParseResponse] The response is null.");

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [ParseResponse] The request is invalid.");

            return request.ProtocolType == ModbusProtocolType.TCP
                ? TryExtractTcpResponse(response, request.SlaveId, request.Length, request.Start, request.FunctionCode, request.Data ?? [])
                : TryExtractRtuResponse(response, request.SlaveId, request.Length, request.Start, request.FunctionCode, request.Data ?? []);
        }

        #region Common methods
        private static bool VerifyReadPdu(ReadOnlySpan<byte> pdu, ModbusFunctionCode functionCode, ushort length)
        {
            int expectedByteCount;
            byte byteCount = pdu[2];

            if (functionCode == ModbusFunctionCode.ReadHodingRegisters || functionCode == ModbusFunctionCode.ReadInputRegisters)
                expectedByteCount = length * 2;
            else
                expectedByteCount = (length + 7) / 8;

            if (byteCount == expectedByteCount)
                return true;

            logger?.Error(" [VerifyReadRx] Byte count mismatch. Expected {expectedByteCount}, actual {byteCount}", expectedByteCount, byteCount);
            return false;
        }

        private static bool VerifySingleWritePdu(ReadOnlySpan<byte> pdu, ushort startAddress, byte[] data)
        {
            var startAdr = BinaryExtensions.ToUshort(pdu[3], pdu[2]);
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

            var frameSpan = pdu.Slice(4, 2);
            return frameSpan.SequenceEqual(data);
        }

        private static bool VerifyMultiWritePdu(ReadOnlySpan<byte> pdu, ushort startAddress, ushort length)
        {
            var start = BinaryExtensions.ToUshort(pdu[3], pdu[2]);
            if (start != startAddress)
            {
                logger?.Error(" [VerifyMultiWriteRx] The start address error. Actual {start}, expected {startAddress}", start, startAddress);
                return false;
            }

            var dataLength = BinaryExtensions.ToUshort(pdu[5], pdu[4]);
            if (dataLength != length)
            {
                logger?.Error(" [VerifyMultiWriteRx] The length error. Actual {dataLength}, expected {length}", dataLength, length);
                return false;
            }

            return true;
        }
        #endregion


        #region TCP response extraction

        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractTcpResponse(ReadOnlyMemory<byte> response, byte slaveID, ushort length,
            ushort startAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            if (response.Length < TcpMinFrameLength)
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            ushort protocolId = BinaryExtensions.ToUshort(response.Span[3], response.Span[2]);
            ushort frameLength = BinaryExtensions.ToUshort(response.Span[5], response.Span[4]);
            byte unitId = response.Span[6];
            byte funcCode = response.Span[7];

            if (protocolId != 0x00)
            {
                logger?.Warning(" [TryExtractTcpRx] Invalid protocol ID: {protocolId}, and span : {@span}", protocolId, response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($"Invalid protocol ID: {protocolId}.", response);
            }

            if (unitId != slaveID)
                logger?.Warning(" [TryExtractTcpRx] The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, and span : {@span}", unitId, slaveID, response.ToArray());

            int totalLength = TcpPduOffset + frameLength;
            if (response.Length < totalLength)
            {
                logger?.Warning(" [TryExtractTcpRx] Invalid response length. Actual {span.Length}, expected {totalLength}, and span : {@span}", response.Length, totalLength, response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail($" [TryExtractTcpRx] Invalid response length. Actual {response.Length}, expected {totalLength}.", response);
            }
            
            if (funcCode == (byte)((byte)functionCode | 0x80))  // 异常验证
                return ModbusResult<ReadOnlyMemory<byte>>.Success(response);

            return CategorizeFunctionCode(functionCode) switch
            {
                FunctionCodeCategory.Read =>
                    HandleTcpRead(response, TcpPduOffset, functionCode, length),

                FunctionCodeCategory.WriteSingle =>
                    HandleTcpWriteSingle(response, TcpPduOffset, startAddr, data),

                FunctionCodeCategory.WriteMulti =>
                    HandleTcpWriteMulti(response, TcpPduOffset, startAddr, length),

                _ => DefaultUnmatchedTcp(response)
            };
        }

        private static ModbusResult<ReadOnlyMemory<byte>> HandleTcpRead(ReadOnlyMemory<byte> response, int pduOffset,
            ModbusFunctionCode functionCode, ushort length)
        {
            int byteCount = response.Span[pduOffset + 2];
            int pduDataLength = 3 + byteCount;
            int expectedLength = pduOffset + pduDataLength;

            if (response.Length < expectedLength)
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            var cutFrame = response[..expectedLength];
            var pduSpan = cutFrame.Span.Slice(pduOffset, pduDataLength);

            if (!VerifyReadPdu(pduSpan, functionCode, length))
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            return ModbusResult<ReadOnlyMemory<byte>>.Success(cutFrame);
        }


        private static ModbusResult<ReadOnlyMemory<byte>> HandleTcpWriteSingle(ReadOnlyMemory<byte> response, int pduOffset,
            ushort startAddr, byte[] data)
        {
            if (data == null || data.Length == 0 || response.Length < pduOffset + 6)
            {
                logger?.Warning(" [TryExtractTcpRx] The data length is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The data length is not valid", response);
            }

            var pduSpan = response.Span.Slice(pduOffset, 6);

            if (!VerifySingleWritePdu(pduSpan, startAddr, data))
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
        }


        private static ModbusResult<ReadOnlyMemory<byte>> HandleTcpWriteMulti(ReadOnlyMemory<byte> response, int pduOffset,
            ushort startAddr, ushort length)
        {
            var pduSpan = response.Span.Slice(pduOffset, 6);

            if (!VerifyMultiWritePdu(pduSpan, startAddr, length))
            {
                logger?.Warning(" [TryExtractTcpRx] The response is not valid : {@span}", response.ToArray());
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The response is not valid", response);
            }

            return ModbusResult<ReadOnlyMemory<byte>>.Success(response);
        }


        private static ModbusResult<ReadOnlyMemory<byte>> DefaultUnmatchedTcp(ReadOnlyMemory<byte> response)
        {
            logger?.Rx(" [TryExtractTcpRx] TCP", response.Span);
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractTcpRx] The function code cannot be matched.", response);
        }

        #endregion


        #region RTU response extraction
        private static ModbusResult<ReadOnlyMemory<byte>> TryExtractRtuResponse(ReadOnlyMemory<byte> response, byte slaveID, ushort length,
            ushort startAddr, ModbusFunctionCode functionCode, byte[] data)
        {
            while (response.Length >= RtuMinFrameLength)
            {
                var id = response.Span[0];
                var funcCode = response.Span[1];

                if (id != slaveID)
                {
                    logger?.Warning(" [TryExtractRtuRx] The actual slave is not matched. Actual {slaveId}, expected {expectedSlaveId}, remove it and continue.", id, slaveID);
                    response = response[1..];
                    continue;
                }

                // Exception response
                if (funcCode == (byte)((byte)functionCode | 0x80))
                {
                    var (exceptionResult, shouldRetry) = HandleRtuException(response);
                    if (!shouldRetry)
                        return exceptionResult;
                    response = response[1..];
                    continue;
                }

                // Normal payload
                var category = CategorizeFunctionCode(functionCode);

                if (category == FunctionCodeCategory.Unknown)
                {
                    logger?.Warning(" [TryExtractRtuRx] Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.", response.ToArray());
                    response = response[1..];
                    continue;
                }

                var (payloadResult, retry) = category switch
                {
                    FunctionCodeCategory.Read =>
                        HandleRtuRead(response, functionCode, length),

                    FunctionCodeCategory.WriteSingle =>
                        HandleRtuWriteSingle(response, startAddr, data),

                    FunctionCodeCategory.WriteMulti =>
                        HandleRtuWriteMulti(response, startAddr, length),

                    _ => (DefaultUnmatchedRtu(response), false)
                };

                if (retry)
                {
                    response = response[1..];
                    continue;
                }

                return payloadResult;
            }

            logger?.Error(" [TryExtractRtuRx] The Rx match failed {@buffer}", response.ToArray());
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The Rx match failed", response);
        }


        private static (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuException(ReadOnlyMemory<byte> response)
        {
            const int exceptionLength = 5;

            if (exceptionLength > response.Length)
            {
                logger?.Error(" [TryExtractRtuRx] The exception response length is not matched. Actual {length}, expected {expectedLength} and buffer : {@buffer}", response.Length, exceptionLength, response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    $"The exception response length is not matched. Actual {response.Length}, expected {exceptionLength} and buffer : {response}",
                    response), false);
            }

            var candidate = response[..exceptionLength];
            if (Crc16Helper.VerifyCrc(candidate.Span))
            {
                logger?.Rx("SerialPort", candidate.Span);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate), false);
            }

            logger?.Warning(" [TryExtractRtuRx] The exception response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[4], candidate.Span[3]);
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The exception response CRC error.", candidate), true);
        }

        private static (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuRead(
            ReadOnlyMemory<byte> response, ModbusFunctionCode functionCode, ushort length)
        {
            int byteCount = response.Span[RtuPduOffset + 2];
            int expectedLength = 3 + byteCount + 2;

            if (response.Length < expectedLength)
            {
                logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    $" [TryExtractRtuRx] The response is not valid : {response}", response), false);
            }

            var candidate = response[..expectedLength];

            if (!VerifyReadPdu(candidate.Span, functionCode, length))
            {
                logger?.Warning("The response is not valid : {@buffer}", response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail("The response is not valid", response), true);
            }

            if (Crc16Helper.VerifyCrc(candidate.Span))
            {
                logger?.Rx("SerialPort", candidate.Span);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate), false);
            }

            logger?.Warning(" [TryExtractRtuRx] The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[^2], candidate.Span[^1]);
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The response CRC error.", candidate), true);
        }

        private static (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuWriteSingle(
            ReadOnlyMemory<byte> response, ushort startAddr, byte[] data)
        {
            if (data == null || data.Length == 0 || response.Length < 8)
            {
                logger?.Warning(" [TryExtractRtuRx] The data length is not valid : {@buffer}", response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [TryExtractRtuRx] The data length is not valid", response), false);
            }

            if (!VerifySingleWritePdu(response.Span, startAddr, data))
            {
                logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [TryExtractRtuRx] The response is not valid", response), true);
            }

            var candidate = response[..8];

            if (Crc16Helper.VerifyCrc(candidate.Span))
            {
                logger?.Rx("SerialPort", candidate.Span);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate), false);
            }

            logger?.Warning(" [TryExtractRtuRx] The response CRC error. High byte: {crcHigh}, Low byte: {crcLow}, remove it and continue.", candidate.Span[^2], candidate.Span[^1]);
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] The response CRC error.", candidate), true);
        }

        private static (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuWriteMulti(
            ReadOnlyMemory<byte> response, ushort startAddr, ushort length)
        {
            if (!VerifyMultiWritePdu(response.Span[..8], startAddr, length))
            {
                logger?.Warning(" [TryExtractRtuRx] The response is not valid : {@buffer}", response.ToArray());
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [TryExtractRtuRx] The response is not valid", response), true);
            }

            return (ModbusResult<ReadOnlyMemory<byte>>.Success(response), false);
        }

        private static ModbusResult<ReadOnlyMemory<byte>> DefaultUnmatchedRtu(ReadOnlyMemory<byte> response)
        {
            logger?.Warning(" [TryExtractRtuRx] Rx length match success, but Rx is not matched. {@Buffer}, remove first byte and continue.", response.ToArray());
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [TryExtractRtuRx] Rx length match success, but Rx is not matched.", response);
        }

        #endregion
    }
}
