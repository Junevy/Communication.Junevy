using Communication.ModBus.Common;
using System.Diagnostics;

namespace Communication.ModBus.Utils
{
    public static class LogExtentions
    {
        private static readonly Stopwatch sw = Stopwatch.StartNew();
        private static long lastTimestamp = 0;

        /// <summary>
        /// 扩展方法：将字节数组格式化为十六进制
        /// </summary>
        /// <param name="bytes">需要格式化的字节数组</param>
        /// <returns>格式化后的结果</returns>
        public static string ToHex(this byte[] bytes)
        {
            string hex = string.Join(Environment.NewLine);

            // 转 16 进制字符串
            var lines = bytes
                .Select((b, i) => new { b, i })
                .GroupBy(x => x.i / 16)
                .Select(g => string.Join("-", g.Select(x => x.b.ToString("X2"))));

            hex = string.Join(Environment.NewLine, lines);

            return hex;
        }

        public static void Tx(this ISerilog logger, string ip, byte[] data)
        {
            long now = sw.ElapsedMilliseconds;

            logger.Debug(
                "[TX] [{IP}] --> {Data}",
                ip,
                data.ToHex()
            );

            lastTimestamp = now;
        }

        ///// <summary>
        ///// 格式接收日志（自动计算时间差）
        ///// </summary>
        public static void Rx(this ISerilog logger, string ip, byte[] data)
        {
            long now = sw.ElapsedMilliseconds;
            long delta = now - lastTimestamp;

            logger.Debug(
                "[RX] [{IP}] <-- {Data} (+{Delta} ms)",
                ip,
                data.ToHex(),
                delta
            );

            lastTimestamp = now;
        }
    }
}
