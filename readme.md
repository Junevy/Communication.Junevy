# Communication.Modbus

Modbus RTU/TCP communication library for .NET. It provides low-level request APIs, convenient extension methods for common function codes, and a factory for managing multiple Modbus connections.

## Target Frameworks

- .NET Framework 4.7.2 (`net472`)
- .NET 6 (`net6.0`)

Project path:

```text
Communication.Modbus/Communication.Modbus.csproj
```

## Supported Function Codes

| Code | Name | Extension method |
|---|---|---|
| `0x01` | Read Coils | `ReadCoils` / `ReadCoilsAsync` |
| `0x02` | Read Discrete Inputs | `ReadDiscreteInputs` / `ReadDiscreteInputsAsync` |
| `0x03` | Read Holding Registers | `ReadHoldingRegisters` / `ReadHoldingRegistersAsync` |
| `0x04` | Read Input Registers | `ReadInputRegisters` / `ReadInputRegistersAsync` |
| `0x05` | Write Single Coil | `WriteSingleCoil` / `WriteSingleCoilAsync` |
| `0x06` | Write Single Register | `WriteSingleRegister` / `WriteSingleRegisterAsync` |
| `0x07` | Read Exception Status | `ReadExceptionStatus` / `ReadExceptionStatusAsync` |
| `0x08` | Diagnostics | `Diagnostics` / `DiagnosticsAsync` |
| `0x0B` | Get Comm Event Counter | `GetCommEventCounter` / `GetCommEventCounterAsync` |
| `0x0C` | Get Comm Event Log | `GetCommEventLog` / `GetCommEventLogAsync` |
| `0x0F` | Write Multiple Coils | `WriteMultipleCoils` / `WriteMultipleCoilsAsync` |
| `0x10` | Write Multiple Registers | `WriteMultipleRegisters` / `WriteMultipleRegistersAsync` |
| `0x11` | Report Server ID | `ReportServerId` / `ReportServerIdAsync` |
| `0x16` | Mask Write Register | `MaskWriteRegister` / `MaskWriteRegisterAsync` |
| `0x17` | Read/Write Multiple Registers | `ReadWriteMultipleRegisters` / `ReadWriteMultipleRegistersAsync` |

## Minimal Usage: Manual `new`

### Modbus TCP

```csharp
using Communication.Modbus.Extensions;
using Communication.Modbus.TCP;

using var modbus = new ModbusTCP(new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port = 502,
    ConnectTimeout = 2000,
    ReadTimeOut = 2000,
    WriteTimeOut = 2000
});

if (!modbus.Connect())
{
    Console.WriteLine("Connect failed.");
    return;
}

var result = modbus.ReadHoldingRegisters(slaveId: 1, start: 0, length: 4);
Console.WriteLine(result.IsSuccess
    ? string.Join(", ", result.Data!)
    : result.ErrorMessage);

modbus.Disconnect();
```

### Modbus RTU

```csharp
using System.IO.Ports;
using Communication.Modbus.Extensions;
using Communication.Modbus.RTU;

using var modbus = new ModbusRTU(new ModbusRTUConfig
{
    PortName = "COM3",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One,
    ReadTimeOut = 2000,
    WriteTimeOut = 2000
});

modbus.Connect();

var write = modbus.WriteSingleRegister(slaveId: 1, start: 100, value: 1234);
Console.WriteLine(write.IsSuccess ? "Write OK" : write.ErrorMessage);

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
    Port = 502
});

plc.Connect();

var coils = await plc.ReadCoilsAsync(slaveId: 1, start: 0, length: 8);
Console.WriteLine(coils.IsSuccess
    ? string.Join(", ", coils.Data!)
    : coils.ErrorMessage);

plc.Disconnect();
```

For RS-485 multi-drop RTU scenarios, register one physical RTU connection and add aliases for logical slave names:

```csharp
factory.GetOrAdd("rs485-bus", new ModbusRTUConfig { PortName = "COM3" });
factory.RegisterAlias("slave-1", "rs485-bus");
factory.RegisterAlias("slave-2", "rs485-bus");
```

## Raw Request API

When a device uses custom behavior, call `Request` / `RequestAsync` directly:

```csharp
using Communication.Modbus.Core.Models;

var raw = plc.Request(new ModbusRequest
{
    SlaveId = 1,
    FunctionCode = ModbusFunctionCode.Diagnostics,
    Data = new byte[] { 0x00, 0x00, 0x12, 0x34 }
});
```

## Notes

- One request at a time is serialized per connection with `SemaphoreSlim`.
- RTU frames use CRC16 verification.
- TCP responses validate MBAP protocol id, transaction id, and unit id.
- Register addresses are protocol-level zero-based addresses.
