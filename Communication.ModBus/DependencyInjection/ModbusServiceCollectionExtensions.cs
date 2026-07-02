using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Core.Parsing;
using Communication.Modbus.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Communication.Modbus.DependencyInjection
{
    /// <summary>
    /// Extension methods for registering Modbus services in the DI container.
    /// </summary>
    public static class ModbusServiceCollectionExtensions
    {
        /// <summary>
        /// Registers <see cref="IModbusFactory"/> and its dependencies as singletons.
        /// </summary>
        public static IServiceCollection AddModbusFactory(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // Ensure a logger factory is available
            services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

            // Register shared PDU verifier
            services.TryAddSingleton<ModbusPduVerifier>();

            // Register protocol-specific parsers
            services.TryAddSingleton<TcpProtocolParser>();
            services.TryAddSingleton<RtuProtocolParser>();

            // Register the connection manager for standalone use
            services.TryAddSingleton<IModbusConnectionManager, ModbusConnectionManager>();

            // Register the factory (receives both parsers via DI)
            services.TryAddSingleton<IModbusFactory, ModbusFactory>();

            return services;
        }
    }
}
