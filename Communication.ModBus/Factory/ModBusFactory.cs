using Communication.Modbus.Core;
using Communication.Modbus.RTU;
using Communication.Modbus.TCP;
using System.Collections.Concurrent;

namespace Communication.Modbus.Factory
{
    /// <summary>
    /// ModBus 工厂，用于创建 ModBus 实例。
    /// </summary>
    public sealed class ModbusFactory : IModbusFactory
    {
        private readonly ConcurrentDictionary<string, IModbus> modbusList = new();


        public bool TryGetMosbus(out IModbus? modbus, string key)
        {
            modbus = default;

            if (string.IsNullOrEmpty(key))
                return false;

            var result = modbusList.TryGetValue(key, out modbus);
            return result;
        }

        public bool TryAddModbus(out IModbus? socket, ModbusTCPConfig config, string? key = null)
        {
            var tcp = new ModbusTCP(config);
            var result = modbusList.TryAdd(key ?? config.Address, tcp);
            if (result)
                socket = tcp;
            else
                throw new Exception("The Modbus TCP instance already exists!");
            return result;
        }

        public bool TryAddModbus(out IModbus? socket, ModbusRTUConfig config, string? key = null)
        {
            socket = default;
            var result = modbusList.TryAdd(key ?? config.PortName, new ModbusRTU(config));
            return result;
        }

        public bool TryRemoveModbus(string key)
        {
            var result = modbusList.TryRemove(key, out var mb);
            if (result) mb?.Dispose();
            return result;
        }
    }
}
