using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace Junevy.Communication.Modbus.Extensions
{
    /// <summary>
    /// Provides extension methods for logging Modbus TX/RX data.
    /// </summary>
    public static class LogExtensions
    {
        /// <summary>
        /// Formats a byte array as grouped hex lines (16 bytes per line).
        /// </summary>
        public static string ToHex(this byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            return ((ReadOnlySpan<byte>)bytes).ToHex();
        }

        public static string ToHex(this ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0)
                return string.Empty;

            var builder = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                    builder.Append(i % 16 == 0 ? Environment.NewLine : '-');

                builder.Append(bytes[i].ToString("X2"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Logs a transmitted Modbus frame with a timestamp.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="name">The connection identifier (IP or port name).</param>
        /// <param name="data">The transmitted data.</param>
        /// <param name="stopwatch">A running stopwatch for time-delta calculation.</param>
        /// <param name="lastTimestamp">Reference to the last recorded timestamp (will be updated).</param>
        public static void Tx(this ILogger logger, string name, byte[] data, Stopwatch stopwatch, ref long lastTimestamp)
            => Tx(logger, name, (ReadOnlySpan<byte>)data, stopwatch, ref lastTimestamp);

        /// <summary>
        /// Logs a transmitted Modbus frame with a timestamp.
        /// </summary>
        public static void Tx(this ILogger logger, string name, ReadOnlySpan<byte> data, Stopwatch stopwatch, ref long lastTimestamp)
        {
            long now = stopwatch.ElapsedMilliseconds;

            logger.LogDebug(
                "[TX] [{Name}] --> {Data}",
                name,
                data.ToHex()
            );

            lastTimestamp = now;
        }

        /// <summary>
        /// Logs a received Modbus frame with the delta from the last recorded timestamp.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="name">The connection identifier (IP or port name).</param>
        /// <param name="data">The received data.</param>
        /// <param name="stopwatch">A running stopwatch for time-delta calculation.</param>
        /// <param name="lastTimestamp">Reference to the last recorded timestamp (will be updated).</param>
        public static void Rx(this ILogger logger, string name, ReadOnlySpan<byte> data, Stopwatch stopwatch, ref long lastTimestamp)
        {
            long now = stopwatch.ElapsedMilliseconds;
            long delta = now - lastTimestamp;

            logger.LogDebug(
                "[RX] [{Name}] <-- {Data} (+{Delta} ms)",
                name,
                data.ToHex(),
                delta
            );

            lastTimestamp = now;
        }
    }
}
