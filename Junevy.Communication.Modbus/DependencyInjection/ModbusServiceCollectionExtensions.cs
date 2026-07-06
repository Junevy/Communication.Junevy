using Junevy.Communication.Modbus.Core.Interfaces;
using Junevy.Communication.Modbus.Core.Framing;
using Junevy.Communication.Modbus.Core.Models;
using Junevy.Communication.Modbus.Core.Parsing;
using Junevy.Communication.Modbus.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Junevy.Communication.Modbus.DependencyInjection
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
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            // Ensure a logger factory is available
            services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

            // Register shared PDU verifier
            services.TryAddSingleton<ModbusPduVerifier>();
            services.TryAddSingleton<IModbusFrameBuilder, ModbusFrameBuilder>();

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
