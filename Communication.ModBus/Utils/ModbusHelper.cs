using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Extensions;

namespace Communication.Modbus.Utils
{
    public static class ModbusHelper
    {
        /// <summary>
        /// Validates a Modbus request frame.
        /// </summary>
        public static bool CheckRequest(ModbusRequest request)
        {
            if (request.Start > 0xFFFF
                || request.Length > 0xFFFF
                || request.SlaveId > 255
                || request.FunctionCode < ModbusFunctionCode.ReadCoils
                || request.FunctionCode > ModbusFunctionCode.WriteMultipleHoldingRegisters)
                return false;

            if (request.FunctionCode < ModbusFunctionCode.WriteCoil ||
                request.FunctionCode > ModbusFunctionCode.WriteMultipleHoldingRegisters) return true;
            return request.Data != null && request.Data.Length > 0;
        }

        /// <summary>
        /// Builds a Modbus request frame from a request object.
        /// </summary>
        /// <exception cref="ModbusException">Thrown when the request is invalid.</exception>
        public static byte[] BuildRequestFrame(ModbusRequest request)
        {
            if (!CheckRequest(request))
                throw new ModbusException(ModbusErrorCode.InvalidValue, "Invalid request.");

            return request.ProtocolType switch
            {
                ModbusProtocolType.RTU => BuildRtuRequestFrame(request),
                ModbusProtocolType.TCP => BuildTcpRequestFrame(request),
                _ => throw new ModbusException(ModbusErrorCode.InvalidValue, "The protocol is not supported.")
            };
        }

        private static byte[] BuildRtuRequestFrame(ModbusRequest request)
        {
            List<byte> frame;

            if (request.FunctionCode >= ModbusFunctionCode.WriteCoil)
            {
                if (request.Data == null || request.Data.Length <= 0)
                    throw new ModbusException(ModbusErrorCode.InvalidData, "The data is empty.");

                // 0x16 Mask Write Register: [SlaveId, 0x16, Start(2), AndMask(2), OrMask(2)]
                if (request.FunctionCode == ModbusFunctionCode.MaskWriteRegister)
                    frame =
                    [
                        request.SlaveId,
                        (byte)request.FunctionCode,
                        .. request.Start.ToBigEndian(),
                        .. request.Data,  // Data = [AndHi, AndLo, OrHi, OrLo]
                    ];

                // Build single-write frame
                else if (request.FunctionCode == ModbusFunctionCode.WriteCoil || request.FunctionCode == ModbusFunctionCode.WriteHoldingRegister)
                    frame =
                    [
                        request.SlaveId,
                        (byte)request.FunctionCode,
                        .. request.Start.ToBigEndian(),
                        .. request.Data,
                    ];

                // Build multi-write frame
                else
                    frame =
                    [
                        request.SlaveId,
                        (byte)request.FunctionCode,
                        .. request.Start.ToBigEndian(),
                        .. request.Length.ToBigEndian(),
                        (byte)(request.FunctionCode == ModbusFunctionCode.WriteMultipleCoils
                                    ? (request.Length + 7) / 8 : (request.Length * 2)),
                        .. request.Data,
                    ];
            }

            // 0x17 Read/Write Multiple Registers: [SlaveId, 0x17, ReadStart(2), ReadQty(2), WriteStart(2), WriteQty(2), WriteByteCount, WriteData...]
            else if (request.FunctionCode == ModbusFunctionCode.ReadWriteMultipleRegisters)
            {
                if (request.Data == null || request.Data.Length <= 0)
                    throw new ModbusException(ModbusErrorCode.InvalidData, "The data is empty.");

                frame =
                [
                    request.SlaveId,
                    (byte)request.FunctionCode,
                    .. request.Data,  // Pre-encoded: [ReadStart, ReadQty, WriteStart, WriteQty, ByteCount, WriteRegData...]
                ];
            }

            // Build read frame
            else
            {
                frame =
                [
                    request.SlaveId,
                    (byte)request.FunctionCode,
                    .. request.Start.ToBigEndian(),
                    .. request.Length.ToBigEndian(),
                ];
            }

            if (frame.Count == 0)
                throw new ModbusException(ModbusErrorCode.InvalidValue, "Check the function code or data.");

            Crc16Helper.AddCrc16(frame);
            return [.. frame];
        }

        private static byte[] BuildTcpRequestFrame(ModbusRequest request)
        {
            var rtuFrame = BuildRtuRequestFrame(request);
            // PDU = RTU frame minus 2-byte CRC (the last 2 bytes are not part of the TCP payload)
            var pdu = rtuFrame.AsSpan(0, rtuFrame.Length - 2);
            var transactionId = (ushort)(request.TransactionId + 0x01);

            List<byte> frame =
            [
                .. transactionId.ToBigEndian(),
                0x00,
                0x00,
                .. ((ushort)pdu.Length).ToBigEndian(),
                .. pdu,
            ];

            return [.. frame];
        }

        /// <summary>
        /// Parses coil values from a Modbus response frame.
        /// </summary>
        public static bool[] ParseCoils(byte[] response, int length)
        {
            if (response == null)
                throw new ModbusException(ModbusErrorCode.InvalidData, "The response data cannot be null.");

            if (length <= 0)
                throw new ModbusException(ModbusErrorCode.InvalidValue, "Length must be greater than 0.");

            int expectedByteCount = (length + 7) / 8;
            if (response.Length < 2 + expectedByteCount)
                throw new ModbusException(ModbusErrorCode.InvalidData,
                    "The response data is not enough for the requested length.");

            bool[] result = new bool[length];
            var start = 3;

            for (int i = 0; i < length; i++)
            {
                var byteIndex = i / 8;
                var bitIndex = i % 8;

                result[i] = ((response[start + byteIndex] >> bitIndex) & 1) == 1;
            }

            return result;
        }

        /// <summary>
        /// Parses register values from a Modbus response frame.
        /// </summary>
        public static ushort[] ParseRegisters(byte[] response, int length)
        {
            if (response == null)
                throw new ModbusException(ModbusErrorCode.InvalidData, "The response data cannot be null.");

            if (length <= 0)
                throw new ModbusException(ModbusErrorCode.InvalidData, "Length must be greater than 0.");

            if (response.Length < 3 + length * 2)
                throw new ModbusException(ModbusErrorCode.InvalidData,
                    "The response data is not enough for the requested length.");

            ushort[] result = new ushort[length];

            for (int i = 0; i < length * 2; i += 2)
            {
                var index = 3 + i;
                result[i / 2] = (ushort)((response[index] << 8) | response[index + 1]);
            }
            return result;
        }

        public static bool VerifyAddress(string address) => !string.IsNullOrEmpty(address);

        public static bool VerifyPort(int port) => (port >= 1024 && port <= 65535) || port == 502;
    }
}
