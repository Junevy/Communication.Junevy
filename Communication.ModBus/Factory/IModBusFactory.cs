using Communication.Modbus.Core;
using Communication.Modbus.RTU;
using Communication.Modbus.TCP;

namespace Communication.Modbus.Factory
{
    public interface IModbusFactory : IDisposable, IAsyncDisposable
    {
        int Count { get; }

        IModbus? Get(string key);

        TResult GetRequired<TResult>(string key) where TResult : class, IModbus;

        IEnumerable<string> Keys { get; }

        IModbus GetOrAdd(string key, ModbusTCPConfig config);

        IModbus GetOrAdd(string key, ModbusRTUConfig config);

        bool TryRemove(string key);

        bool TryGet(string key, out IModbus? modbus);

        bool TryAdd(string key, ModbusTCPConfig config, out IModbus? modbus);

        bool TryAdd(string key, ModbusRTUConfig config, out IModbus? modbus);
    }
}



