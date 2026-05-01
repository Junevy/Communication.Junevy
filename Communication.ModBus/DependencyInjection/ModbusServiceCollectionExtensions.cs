using Communication.Modbus.Factory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Communication.Modbus.DependencyInjection
{
    public static class ModbusServiceCollectionExtensions
    {
        public static IServiceCollection AddModbusFactory(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IModbusFactory, ModbusFactory>();

            return services;
        }
    }
}
