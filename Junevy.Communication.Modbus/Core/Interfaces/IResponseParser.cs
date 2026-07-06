using Junevy.Communication.Modbus.Core.Models;

namespace Junevy.Communication.Modbus.Core.Interfaces
{
    /// <summary>
    /// Parses raw Modbus response data into validated results.
    /// </summary>
    public interface IResponseParser
    {
        /// <summary>
        /// Parses a raw Modbus response against the original request.
        /// </summary>
        ModbusResult<ReadOnlyMemory<byte>> ParseResponse(ReadOnlyMemory<byte> response, ModbusRequest request);
    }
}
