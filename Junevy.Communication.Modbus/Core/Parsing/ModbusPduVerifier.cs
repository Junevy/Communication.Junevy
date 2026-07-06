using Junevy.Communication.Modbus.Core.Models;
using Junevy.Communication.Modbus.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Junevy.Communication.Modbus.Core.Parsing
{
    /// <summary>
    /// Shared PDU verification and function-code categorization logic.
    /// Used by both TCP and RTU protocol parsers — extracted to avoid duplication.
    /// Registered as a singleton in the DI container.
    /// </summary>
    public class ModbusPduVerifier
    {
        public enum FunctionCodeCategory
        {
            Read,
            WriteSingle,
            WriteMulti,
            Unknown
        }

        private readonly ILogger<ModbusPduVerifier> logger;

        public ModbusPduVerifier(ILogger<ModbusPduVerifier>? logger = null)
        {
            this.logger = logger ?? NullLogger<ModbusPduVerifier>.Instance;
        }

        internal FunctionCodeCategory CategorizeFunctionCode(ModbusFunctionCode functionCode)
        {
            if (functionCode >= ModbusFunctionCode.ReadCoils && functionCode <= ModbusFunctionCode.ReadInputRegisters)
                return FunctionCodeCategory.Read;
            if (functionCode >= ModbusFunctionCode.WriteCoil && functionCode <= ModbusFunctionCode.WriteHoldingRegister)
                return FunctionCodeCategory.WriteSingle;
            if (functionCode >= ModbusFunctionCode.WriteMultipleCoils && functionCode <= ModbusFunctionCode.WriteMultipleHoldingRegisters)
                return FunctionCodeCategory.WriteMulti;
            // 0x16 MaskWriteRegister → echoes request (WriteSingle path)
            if (functionCode == ModbusFunctionCode.MaskWriteRegister)
                return FunctionCodeCategory.WriteSingle;
            // 0x17 ReadWriteMultipleRegisters → returns read data (Read path)
            if (functionCode == ModbusFunctionCode.ReadWriteMultipleRegisters)
                return FunctionCodeCategory.Read;
            return FunctionCodeCategory.Unknown;
        }

        internal bool VerifyReadPdu(ReadOnlySpan<byte> pdu, ModbusFunctionCode functionCode, ushort length)
        {
            int expectedByteCount;
            byte byteCount = pdu[2];

            if (functionCode == ModbusFunctionCode.ReadHoldingRegisters
                || functionCode == ModbusFunctionCode.ReadInputRegisters
                || functionCode == ModbusFunctionCode.ReadWriteMultipleRegisters)
                expectedByteCount = length * 2;
            else
                expectedByteCount = (length + 7) / 8;

            if (byteCount == expectedByteCount)
            {
                logger.LogDebug(" [VerifyReadPdu] Read successful.");
                return true;
            }

            logger.LogWarning(" [VerifyReadPdu] Byte count mismatch. Expected {Expected}, actual {Actual}.", expectedByteCount, byteCount);
            return false;
        }

        internal bool VerifySingleWritePdu(ReadOnlySpan<byte> pdu, ushort startAddress, byte[] data)
        {
            var startAdr = BinaryExtensions.ToUshort(pdu[3], pdu[2]);
            if (startAdr != startAddress)
            {
                logger.LogWarning(" [VerifySingleWritePdu] Start address mismatch. Expected {Expected}, actual {Actual}.", startAddress, startAdr);
                return false;
            }

            if (data.Length != 2)
            {
                logger.LogWarning(" [VerifySingleWritePdu] Invalid data length: {Length}.", data.Length);
                return false;
            }

            var frameSpan = pdu.Slice(4, 2);
            return frameSpan[0] == data[0] && frameSpan[1] == data[1];
        }

        internal bool VerifyMultiWritePdu(ReadOnlySpan<byte> pdu, ushort startAddress, ushort length)
        {
            var start = BinaryExtensions.ToUshort(pdu[3], pdu[2]);
            if (start != startAddress)
            {
                logger.LogWarning(" [VerifyMultiWritePdu] Start address mismatch. Expected {Expected}, actual {Actual}.", startAddress, start);
                return false;
            }

            var dataLength = BinaryExtensions.ToUshort(pdu[5], pdu[4]);
            if (dataLength != length)
            {
                logger.LogWarning(" [VerifyMultiWritePdu] Length mismatch. Expected {Expected}, actual {Actual}.", length, dataLength);
                return false;
            }

            logger.LogDebug(" [VerifyMultiWritePdu] Write multiple successful.");
            return true;
        }

        /// <summary>
        /// Verifies a Mask Write Register (0x16) response PDU.
        /// The response echoes the request: [FuncCode, Start(2), AndMask(2), OrMask(2)].
        /// </summary>
        internal bool VerifyMaskWritePdu(ReadOnlySpan<byte> pdu, ushort startAddress, ushort andMask, ushort orMask)
        {
            // pdu layout (starting from UnitId): [UnitId, 0x16, StartHi, StartLo, AndHi, AndLo, OrHi, OrLo]
            int expectedLength = 8; // UnitId + FuncCode + Start(2) + AndMask(2) + OrMask(2)
            if (pdu.Length < expectedLength)
            {
                logger.LogWarning(" [VerifyMaskWritePdu] PDU too short: {Actual} < {Expected}.", pdu.Length, expectedLength);
                return false;
            }

            var actualStart = BinaryExtensions.ToUshort(pdu[3], pdu[2]);
            if (actualStart != startAddress)
            {
                logger.LogWarning(" [VerifyMaskWritePdu] Start address mismatch. Expected {Expected}, actual {Actual}.", startAddress, actualStart);
                return false;
            }

            var actualAnd = BinaryExtensions.ToUshort(pdu[5], pdu[4]);
            if (actualAnd != andMask)
            {
                logger.LogWarning(" [VerifyMaskWritePdu] AND mask mismatch. Expected {Expected}, actual {Actual}.", andMask, actualAnd);
                return false;
            }

            var actualOr = BinaryExtensions.ToUshort(pdu[7], pdu[6]);
            if (actualOr != orMask)
            {
                logger.LogWarning(" [VerifyMaskWritePdu] OR mask mismatch. Expected {Expected}, actual {Actual}.", orMask, actualOr);
                return false;
            }

            logger.LogDebug(" [VerifyMaskWritePdu] Mask write successful.");
            return true;
        }
    }
}
