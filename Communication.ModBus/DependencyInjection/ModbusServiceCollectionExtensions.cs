using Communication.Modbus.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Communication.Modbus.DependencyInjection
{
    public static class ModbusServiceCollectionExtensions
    {
        public static IServiceCollection AddModbusFactory(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.TryAddSingleton<IModbusFactory, ModbusFactory>();
            return services;
        }
    }
}
