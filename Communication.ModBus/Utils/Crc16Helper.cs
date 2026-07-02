using Communication.Modbus.Extensions;
using System.Buffers.Binary;

namespace Communication.Modbus.Utils
{
    /// <summary>
    /// CRC16 helper methods for Modbus RTU frames.
    /// </summary>
    public static class Crc16Helper
    {
        public static bool VerifyCrc(byte[] frame)
            => VerifyCrc((ReadOnlySpan<byte>)frame);

        public static bool VerifyCrc(ReadOnlySpan<byte> frame)
        {
            if (frame.Length < 2)
                return false;

            ushort receivedCRC = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(frame.Length - 2, 2));
            ushort calculatedCRC = ComputeCrc(frame.Slice(0, frame.Length - 2));
            return receivedCRC == calculatedCRC;
        }

        public static void AddCrc16(List<byte> frame)
        {
            frame.AddRange(CrcLittleEndian(frame.ToArray()));
        }

        public static ushort ComputeCrc(byte[] data)
            => ComputeCrc((ReadOnlySpan<byte>)data);

        public static ushort ComputeCrc(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];

                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }

        public static byte[] CrcLittleEndian(byte[] data)
        {
            ushort crc = ComputeCrc(data);
            return crc.ToLittleEndian();
        }

        public static byte[] CrcLittleEndian(ReadOnlySpan<byte> data)
        {
            ushort crc = ComputeCrc(data);
            return crc.ToLittleEndian();
        }

        public static byte[] CrcBigEndian(byte[] data)
        {
            ushort crc = ComputeCrc(data);
            return BinaryExtensions.ToBigEndian(crc);
        }
    }
}
