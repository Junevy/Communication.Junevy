using Junevy.Communication.Modbus.Core.Models;

namespace Junevy.Communication.Modbus.Core.Interfaces
{
    /// <summary>
    /// Builds Modbus request frames for protocol transports.
    /// </summary>
    public interface IModbusFrameBuilder
    {
        /// <summary>
        /// Gets the exact request ADU length for the configured protocol.
        /// </summary>
        int GetRequestFrameLength(ModbusRequest request);

        /// <summary>
        /// Writes the request ADU into the supplied buffer.
        /// </summary>
        bool TryWriteRequestFrame(ModbusRequest request, Span<byte> destination, out int bytesWritten);

        /// <summary>
        /// Builds a request ADU as a new array for compatibility APIs.
        /// </summary>
        byte[] BuildRequestFrame(ModbusRequest request);
    }
}
