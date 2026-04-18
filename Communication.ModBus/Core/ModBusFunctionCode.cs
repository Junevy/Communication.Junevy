namespace Communication.Modbus.Core
{
    public enum ModbusFunctionCode
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHodingRegisters = 0x03,
        ReadInputRegisters = 0x04,
        WriteCoil = 0x05,
        WriteHodingRegister = 0x06,
        WriteMultiCoils = 0x0F,
        WriteMultiHodingRegisters = 0x10,
    }
}
