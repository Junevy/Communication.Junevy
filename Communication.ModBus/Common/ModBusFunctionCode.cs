namespace Communication.ModBus.Common
{
    public enum ModBusFunctionCode
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHodingRegister = 0x03,
        ReadInputRegister = 0x04,
        WriteCoils = 0x05,
        WriteHodingRegister = 0x06,
        WriteMultiCoils = 0x0F,
        WriteMultiHodingRegister = 0x10,
    }
}
