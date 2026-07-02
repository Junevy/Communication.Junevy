namespace Communication.Modbus.Core.Interfaces
{
    /// <summary>
    /// Common configuration interface shared by Modbus TCP and RTU configs.
    /// </summary>
    public interface IModbusConfig
    {
        /// <summary>
        /// Read timeout in milliseconds.
        /// </summary>
        int ReadTimeOut { get; set; }

        /// <summary>
        /// Write timeout in milliseconds.
        /// </summary>
        int WriteTimeOut { get; set; }

        /// <summary>
        /// Number of retry attempts on communication failure.
        /// </summary>
        int RetryCount { get; set; }

        /// <summary>
        /// Whether to reopen the physical connection automatically before retrying.
        /// </summary>
        bool Reconnect { get; set; }

        /// <summary>
        /// Delay in milliseconds between retry/reconnect attempts.
        /// </summary>
        int RetryInterval { get; set; }
    }
}
