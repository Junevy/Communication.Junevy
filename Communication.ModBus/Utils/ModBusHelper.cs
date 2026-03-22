namespace Communication.ModBus.Utils
{
    public static class ModBusHelper
    {
        public static bool ValidateCRC(byte[] frame)
        {
            var dataWithoutCRC = frame.Take(frame.Length - 2).ToArray();
            var receivedCRC = frame.Skip(frame.Length - 2).ToArray();
            var calculatedCRC = CRC16.CRCLittleEndian(dataWithoutCRC);
            return receivedCRC.SequenceEqual(calculatedCRC);
        }

        public static byte[] BuildReadFrame(byte slaveID, byte functionCode, ushort start, ushort length)
        {
            List<byte> frame =
            [
                slaveID,
                functionCode,
                .. UshortHelper.ToBytesByBigEndian(start),
                .. UshortHelper.ToBytesByBigEndian(length),
            ];

            AddCRC16(frame);
            return frame.ToArray();
        }

        public static void AddCRC16(List<byte> frame)
            => frame.AddRange(CRC16.CRCLittleEndian(frame.ToArray()));
    }
}
