namespace Communication.Modbus.Core.Models
{
    public enum ModbusFunctionCode
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHoldingRegisters = 0x03,
        ReadInputRegisters = 0x04,
        WriteCoil = 0x05,
        WriteHoldingRegister = 0x06,
        WriteMultipleCoils = 0x0F,
        WriteMultipleHoldingRegisters = 0x10,
        MaskWriteRegister = 0x16,
        ReadWriteMultipleRegisters = 0x17,
    }
}
