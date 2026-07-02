using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Extensions;
using Communication.Modbus.Utils;
using Communication.ModBus.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Communication.Modbus.Core.Parsing
{
    /// <summary>
    /// Parses Modbus RTU response frames. Implements <see cref="IResponseParser"/> directly
    /// so it can be injected into <see cref="RTU.ModbusRTU"/> as its response parser.
    /// </summary>
    public sealed class RtuProtocolParser : IResponseParser
    {
        private const int RtuMinFrameLength = 5;

        private readonly ILogger<RtuProtocolParser> logger;
        private readonly ModbusPduVerifier verifier;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private long lastTimestamp;

        public RtuProtocolParser(
            ILogger<RtuProtocolParser>? logger = null,
            ModbusPduVerifier? verifier = null)
        {
            this.logger = logger ?? NullLogger<RtuProtocolParser>.Instance;
            this.verifier = verifier ?? new ModbusPduVerifier();
        }

        public ModbusResult<ReadOnlyMemory<byte>> ParseResponse(ReadOnlyMemory<byte> response, ModbusRequest request)
        {
            // Common validation
            if (response.Length == 0)
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [RtuParser] The response is empty.");

            if (!ModbusHelper.CheckRequest(request))
                return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [RtuParser] The request is invalid.");

            // RTU-specific: scan with Span offset (zero-copy)
            var span = response.Span;
            int offset = 0;

            while (offset + RtuMinFrameLength <= span.Length)
            {
                var remaining = span.Slice(offset);

                byte id = remaining[0];
                byte funcCode = remaining[1];

                if (id != request.SlaveId)
                {
                    logger.LogWarning(" [RtuParser] Slave ID mismatch. Expected {Expected}, actual {Actual}. Skipping byte.", request.SlaveId, id);
                    offset++;
                    continue;
                }

                // Exception response
                if (funcCode == (byte)((byte)request.FunctionCode | 0x80))
                {
                    var (exceptionResult, shouldRetry) = HandleRtuException(remaining);
                    if (!shouldRetry)
                        return exceptionResult;
                    offset++;
                    continue;
                }

                var category = verifier.CategorizeFunctionCode(request.FunctionCode);
                if (category == ModbusPduVerifier.FunctionCodeCategory.Unknown)
                {
                    logger.LogWarning(" [RtuParser] Unknown function code. Skipping byte.");
                    offset++;
                    continue;
                }

                var data = request.Data ?? [];

                // 0x16 Mask Write Register — special handling (response is an echo with 4 data bytes)
                if (request.FunctionCode == ModbusFunctionCode.MaskWriteRegister)
                {
                    var maskResult = HandleRtuMaskWrite(remaining, request.Start, data);
                    if (maskResult.Retry)
                    {
                        offset++;
                        continue;
                    }
                    return maskResult.Result;
                }

                var (payloadResult, retry) = category switch
                {
                    ModbusPduVerifier.FunctionCodeCategory.Read =>
                        HandleRtuRead(remaining, request.FunctionCode, request.Length),

                    ModbusPduVerifier.FunctionCodeCategory.WriteSingle =>
                        HandleRtuWriteSingle(remaining, request.Start, data),

                    ModbusPduVerifier.FunctionCodeCategory.WriteMulti =>
                        HandleRtuWriteMulti(remaining, request.Start, request.Length),

                    _ => DefaultUnmatched(remaining)
                };

                if (retry)
                {
                    offset++;
                    continue;
                }

                return payloadResult;
            }

            logger.LogError(" [RtuParser] Failed to match response in buffer.");
            return ModbusResult<ReadOnlyMemory<byte>>.Fail(" [RtuParser] Failed to match response.", response);
        }

        private (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuException(ReadOnlySpan<byte> response)
        {
            const int exceptionLength = 5;

            if (exceptionLength > response.Length)
            {
                logger.LogWarning(" [HandleRtuException] Exception response too short: {Actual} < {Expected}.", response.Length, exceptionLength);
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    $"Exception response too short. Expected {exceptionLength}, actual {response.Length}.", response.ToArray()), false);
            }

            var candidate = response[..exceptionLength];
            if (Crc16Helper.VerifyCrc(candidate))
            {
                logger.Rx("SerialPort", candidate, stopwatch, ref lastTimestamp);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate.ToArray()), false);
            }

            logger.LogWarning(" [HandleRtuException] CRC verification failed. Skipping byte.");
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [HandleRtuException] CRC verification failed.", candidate.ToArray()), true);
        }

        private (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuRead(
            ReadOnlySpan<byte> response, ModbusFunctionCode functionCode, ushort length)
        {
            int byteCount = response[2];
            int expectedLength = 3 + byteCount + 2;

            if (response.Length < expectedLength)
            {
                logger.LogWarning(" [HandleRtuRead] Response too short: {Actual} < {Expected}.", response.Length, expectedLength);
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    $"Response too short. Expected {expectedLength}, actual {response.Length}.", response.ToArray()), false);
            }

            var candidate = response[..expectedLength];

            if (!verifier.VerifyReadPdu(candidate, functionCode, length))
            {
                logger.LogWarning(" [HandleRtuRead] PDU verification failed.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [HandleRtuRead] PDU verification failed.", response.ToArray()), true);
            }

            if (Crc16Helper.VerifyCrc(candidate))
            {
                logger.Rx("SerialPort", candidate, stopwatch, ref lastTimestamp);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate.ToArray()), false);
            }

            logger.LogWarning(" [HandleRtuRead] CRC verification failed. Skipping byte.");
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [HandleRtuRead] CRC verification failed.", candidate.ToArray()), true);
        }

        private (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuWriteSingle(
            ReadOnlySpan<byte> response, ushort startAddr, byte[] data)
        {
            if (data == null || data.Length == 0 || response.Length < 8)
            {
                logger.LogWarning(" [HandleRtuWriteSingle] Invalid data or response length.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [HandleRtuWriteSingle] Invalid data or response length.", response.ToArray()), false);
            }

            if (!verifier.VerifySingleWritePdu(response, startAddr, data))
            {
                logger.LogWarning(" [HandleRtuWriteSingle] PDU verification failed.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [HandleRtuWriteSingle] PDU verification failed.", response.ToArray()), true);
            }

            var candidate = response[..8];

            if (Crc16Helper.VerifyCrc(candidate))
            {
                logger.Rx("SerialPort", candidate, stopwatch, ref lastTimestamp);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate.ToArray()), false);
            }

            logger.LogWarning(" [HandleRtuWriteSingle] CRC verification failed. Skipping byte.");
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [HandleRtuWriteSingle] CRC verification failed.", candidate.ToArray()), true);
        }

        private (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuMaskWrite(
            ReadOnlySpan<byte> response, ushort startAddr, byte[] data)
        {
            // Data = [AndHi, AndLo, OrHi, OrLo]
            if (data == null || data.Length != 4 || response.Length < 10)
            {
                logger.LogWarning(" [HandleRtuMaskWrite] Invalid data or response length.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [HandleRtuMaskWrite] Invalid data or response length.", response.ToArray()), false);
            }

            var andMask = BinaryExtensions.ToUshort(data[1], data[0]);
            var orMask = BinaryExtensions.ToUshort(data[3], data[2]);

            if (!verifier.VerifyMaskWritePdu(response, startAddr, andMask, orMask))
            {
                logger.LogWarning(" [HandleRtuMaskWrite] PDU verification failed.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [HandleRtuMaskWrite] PDU verification failed.", response.ToArray()), true);
            }

            var candidate = response[..10]; // SlaveId + FuncCode + Start(2) + And(2) + Or(2) + CRC(2)

            if (Crc16Helper.VerifyCrc(candidate))
            {
                logger.Rx("SerialPort", candidate, stopwatch, ref lastTimestamp);
                return (ModbusResult<ReadOnlyMemory<byte>>.Success(candidate.ToArray()), false);
            }

            logger.LogWarning(" [HandleRtuMaskWrite] CRC verification failed. Skipping byte.");
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [HandleRtuMaskWrite] CRC verification failed.", candidate.ToArray()), true);
        }

        private (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) HandleRtuWriteMulti(
            ReadOnlySpan<byte> response, ushort startAddr, ushort length)
        {
            if (!verifier.VerifyMultiWritePdu(response[..8], startAddr, length))
            {
                logger.LogWarning(" [HandleRtuWriteMulti] PDU verification failed.");
                return (ModbusResult<ReadOnlyMemory<byte>>.Fail(
                    " [HandleRtuWriteMulti] PDU verification failed.", response.ToArray()), true);
            }

            return (ModbusResult<ReadOnlyMemory<byte>>.Success(response.ToArray()), false);
        }

        private static (ModbusResult<ReadOnlyMemory<byte>> Result, bool Retry) DefaultUnmatched(ReadOnlySpan<byte> response)
        {
            return (ModbusResult<ReadOnlyMemory<byte>>.Fail(" [DefaultUnmatched] Unknown function code.", response.ToArray()), false);
        }
    }
}
