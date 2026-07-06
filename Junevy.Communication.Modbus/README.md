# Communication.Modbus

Modbus RTU/TCP communication library for .NET.

## Target Frameworks

- .NET Framework 4.7.2 (`net472`)
- .NET 6 (`net6.0`)

## Minimal Usage: Manual `new`

```csharp
using Communication.Modbus.Extensions;
using Communication.Modbus.TCP;

using var modbus = new ModbusTCP(new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port = 502
});

modbus.Connect();

var registers = modbus.ReadHoldingRegisters(slaveId: 1, start: 0, length: 4);
Console.WriteLine(registers.IsSuccess
    ? string.Join(", ", registers.Data!)
    : registers.ErrorMessage);

modbus.Disconnect();
```

## Recommended Usage: Factory + DI

```csharp
using Communication.Modbus.DependencyInjection;
using Communication.Modbus.Extensions;
using Communication.Modbus.Factory;
using Communication.Modbus.TCP;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddLogging();
services.AddModbusFactory();

using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IModbusFactory>();

var plc = factory.GetOrAdd("plc-1", new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port = 502,
    Reconnect = true,
    RetryCount = 3,
    RetryInterval = 200
});

plc.Connect();
var coils = await plc.ReadCoilsAsync(slaveId: 1, start: 0, length: 8);
plc.Disconnect();
```

## Common Function Codes

`0x01`, `0x02`, `0x03`, `0x04`, `0x05`, `0x06`, `0x07`, `0x08`, `0x0B`, `0x0C`, `0x0F`, `0x10`, `0x11`, `0x16`, and `0x17` are exposed through extension methods in `Communication.Modbus.Extensions`.

## Reconnect

Set `Reconnect = true` and tune `RetryCount` / `RetryInterval` to enable request-level retry and automatic reconnect. TCP sockets are recreated after closed/timeout failures; RTU serial ports are closed and reopened before retrying.
