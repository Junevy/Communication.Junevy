using Communication.Modbus.Core.Framing;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Core.Parsing;
using Communication.Modbus.RTU;
using Communication.Modbus.TCP;
using Communication.Modbus.Utils;

namespace Communication.Modbus.Tests
{
    public class ModbusProtocolTests
    {
        [Fact]
        public void BuildRequestFrame_MaskWriteRegister_RtuFrameIsValid()
        {
            var request = new ModbusRequest
            {
                ProtocolType = ModbusProtocolType.RTU,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.MaskWriteRegister,
                Start = 0x1234,
                Data = [0xFF, 0x00, 0x00, 0xF0]
            };

            var frame = ModbusHelper.BuildRequestFrame(request);

            Assert.Equal(10, frame.Length);
            Assert.Equal([0x01, 0x16, 0x12, 0x34, 0xFF, 0x00, 0x00, 0xF0], frame[..8]);
            Assert.True(Crc16Helper.VerifyCrc(frame));
        }

        [Fact]
        public void BuildRequestFrame_ReadWriteMultipleRegisters_UsesPreEncodedData()
        {
            var request = new ModbusRequest
            {
                ProtocolType = ModbusProtocolType.RTU,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.ReadWriteMultipleRegisters,
                Data =
                [
                    0x00, 0x10,
                    0x00, 0x02,
                    0x00, 0x20,
                    0x00, 0x01,
                    0x02,
                    0x12, 0x34
                ]
            };

            var frame = ModbusHelper.BuildRequestFrame(request);

            Assert.Equal(15, frame.Length);
            Assert.Equal(0x17, frame[1]);
            Assert.True(Crc16Helper.VerifyCrc(frame));
        }

        [Fact]
        public void FrameBuilder_TryWriteTcpRequestFrame_WritesWithoutCompatibilityArray()
        {
            var request = new ModbusRequest
            {
                ProtocolType = ModbusProtocolType.TCP,
                TransactionId = 0,
                SlaveId = 2,
                FunctionCode = ModbusFunctionCode.ReadHoldingRegisters,
                Start = 0x0010,
                Length = 2
            };
            var builder = new ModbusFrameBuilder();
            Span<byte> destination = stackalloc byte[ModbusFrameBuilder.MaxTcpAduLength];

            Assert.True(builder.TryWriteRequestFrame(request, destination, out int written));

            Assert.Equal(12, written);
            Assert.Equal([0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x02, 0x03, 0x00, 0x10, 0x00, 0x02],
                destination[..written].ToArray());
        }

        [Fact]
        public void RtuParser_WriteMultiShortFrame_ReturnsFailureInsteadOfThrowing()
        {
            var parser = new RtuProtocolParser();
            var request = new ModbusRequest
            {
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.WriteMultipleHoldingRegisters,
                Start = 0x0010,
                Length = 2,
                Data = [0x00, 0x01, 0x00, 0x02]
            };

            byte[] response = [0x01, 0x10, 0x00, 0x10, 0x00];
            var result = parser.ParseResponse(response, request);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void RtuParser_WriteMultiInvalidCrc_ReturnsFailure()
        {
            var parser = new RtuProtocolParser();
            var request = new ModbusRequest
            {
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.WriteMultipleHoldingRegisters,
                Start = 0x0010,
                Length = 2,
                Data = [0x00, 0x01, 0x00, 0x02]
            };
            byte[] response = [0x01, 0x10, 0x00, 0x10, 0x00, 0x02, 0x00, 0x00];

            var result = parser.ParseResponse(response, request);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void RtuParser_WriteMultiValidCrc_ReturnsExactFrame()
        {
            var parser = new RtuProtocolParser();
            var request = new ModbusRequest
            {
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.WriteMultipleHoldingRegisters,
                Start = 0x0010,
                Length = 2,
                Data = [0x00, 0x01, 0x00, 0x02]
            };
            byte[] response = [0x01, 0x10, 0x00, 0x10, 0x00, 0x02, 0x00, 0x00];
            var crc = Crc16Helper.CrcLittleEndian(response.AsSpan(0, 6));
            response[6] = crc[0];
            response[7] = crc[1];

            var result = parser.ParseResponse(response, request);

            Assert.True(result.IsSuccess);
            Assert.Equal(8, result.Data.Length);
            Assert.Equal(response, result.Data.ToArray());
        }

        [Fact]
        public void TcpParser_SlaveMismatch_ReturnsFailure()
        {
            var parser = new TcpProtocolParser();
            var request = new ModbusRequest
            {
                TransactionId = 0,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.ReadHoldingRegisters,
                Start = 0,
                Length = 1
            };
            byte[] response = [0x00, 0x01, 0x00, 0x00, 0x00, 0x05, 0x02, 0x03, 0x02, 0x12, 0x34];

            var result = parser.ParseResponse(response, request);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void TcpParser_WriteMultiShortFrame_ReturnsFailureInsteadOfThrowing()
        {
            var parser = new TcpProtocolParser();
            var request = new ModbusRequest
            {
                TransactionId = 0,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.WriteMultipleHoldingRegisters,
                Start = 0x0010,
                Length = 2,
                Data = [0x00, 0x01, 0x00, 0x02]
            };
            byte[] response = [0x00, 0x01, 0x00, 0x00, 0x00, 0x03, 0x01, 0x10, 0x00];

            var result = parser.ParseResponse(response, request);

            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void TcpConfig_SetPort_AllowsWellKnownModbusPort()
        {
            var config = new ModbusTCPConfig();

            Assert.True(config.SetPort(502));
            Assert.Equal(502, config.Port);
        }

        [Fact]
        public void BuildRequestFrame_ReadExceptionStatus_RtuFrameIsValid()
        {
            var request = new ModbusRequest
            {
                ProtocolType = ModbusProtocolType.RTU,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.ReadExceptionStatus
            };

            var frame = ModbusHelper.BuildRequestFrame(request);

            Assert.Equal(4, frame.Length);
            Assert.Equal(0x07, frame[1]);
            Assert.True(Crc16Helper.VerifyCrc(frame));
        }

        [Fact]
        public void BuildRequestFrame_Diagnostics_WritesSubFunctionAndData()
        {
            var request = new ModbusRequest
            {
                ProtocolType = ModbusProtocolType.RTU,
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.Diagnostics,
                Data = [0x00, 0x00, 0x12, 0x34]
            };

            var frame = ModbusHelper.BuildRequestFrame(request);

            Assert.Equal([0x01, 0x08, 0x00, 0x00, 0x12, 0x34], frame[..6]);
            Assert.True(Crc16Helper.VerifyCrc(frame));
        }

        [Fact]
        public void RtuParser_ReportServerId_ReturnsVariableLengthFrame()
        {
            var parser = new RtuProtocolParser();
            var request = new ModbusRequest
            {
                SlaveId = 1,
                FunctionCode = ModbusFunctionCode.ReportServerId
            };
            byte[] response = [0x01, 0x11, 0x03, 0x42, 0xFF, 0x10, 0x00, 0x00];
            var crc = Crc16Helper.CrcLittleEndian(response.AsSpan(0, 6));
            response[6] = crc[0];
            response[7] = crc[1];

            var result = parser.ParseResponse(response, request);

            Assert.True(result.IsSuccess);
            Assert.Equal(response, result.Data.ToArray());
        }

        [Fact]
        public void RtuTransport_DefaultRequestProtocol_IsNotRequiredForValidation()
        {
            var request = new ModbusRequest
            {
                FunctionCode = ModbusFunctionCode.ReadHoldingRegisters,
                Start = 0,
                Length = 1
            };

            Assert.True(ModbusHelper.CheckRequest(request));
        }
    }
}
