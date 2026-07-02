using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Communication.ModBus.Extensions
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

            var lines = bytes
                .Select((b, i) => new { b, i })
                .GroupBy(x => x.i / 16)
                .Select(g => string.Join("-", g.Select(x => x.b.ToString("X2"))));

            return string.Join(Environment.NewLine, lines);
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
                data.ToArray().ToHex(),
                delta
            );

            lastTimestamp = now;
        }
    }
}
