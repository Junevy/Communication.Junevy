# Communication.ModBus

A modern, high-performance .NET library for Modbus TCP and RTU communication. Provides a
unified API for reading/writing coils, discrete inputs, holding registers, and input registers
with built-in connection pooling, DI integration, strategy-pattern response parsing, multi-slave
alias support, and full async support.

**Protocols:** Modbus TCP (via Ethernet) · Modbus RTU (via Serial Port)
**Target Framework:** .NET 8.0+ · **Language:** C# 13 · **License:** MIT

---

## 目录结构

```
communication.modbus/
├── Core/
│   ├── Interfaces/                         # 核心抽象
│   │   ├── IModbus.cs                      # 统一通信接口（TCP/RTU）
│   │   ├── IModbusConfig.cs                # 通用配置接口
│   │   └── IResponseParser.cs              # 响应解析接口
│   ├── Models/                             # 领域模型
│   │   ├── ModbusRequest.cs                # 请求数据载体
│   │   ├── ModbusResult.cs                 # 泛型操作结果
│   │   ├── ModbusFunctionCode.cs           # 功能码枚举 (0x01–0x17)
│   │   ├── ModbusProtocolType.cs           # 协议类型
│   │   └── ModbusException.cs              # 统一异常（含 ModbusErrorCode）
│   └── Parsing/                            # 协议解析（策略模式）
│       ├── ModbusPduVerifier.cs            # PDU 校验（DI 单例）
│       ├── TcpProtocolParser.cs            # TCP 报文解析策略
│       └── RtuProtocolParser.cs            # RTU 报文解析策略
├── TCP/
│   ├── ModbusTCP.cs                        # Socket + PipeReader 通信引擎
│   └── ModbusTCPConfig.cs                  # TCP 配置
├── RTU/
│   ├── ModbusRTU.cs                        # SerialPort 通信引擎
│   └── ModbusRTUConfig.cs                  # RTU 配置
├── Extensions/
│   ├── ModbusExtensions.cs                 # 功能码扩展方法（同步 + 异步）
│   ├── BinaryExtensions.cs                 # 大小端序 / 十六进制转换
│   └── LogExtensions.cs                    # 结构化日志扩展
├── Factory/
│   ├── IModbusFactory.cs                   # 工厂接口
│   ├── ModbusFactory.cs                    # 工厂实现
│   ├── IModbusConnectionManager.cs         # 连接管理器接口（支持多从站别名）
│   └── ModbusConnectionManager.cs          # 连接管理器实现
├── Utils/
│   ├── ModbusHelper.cs                     # 帧构建 / 解析
│   └── Crc16Helper.cs                      # CRC-16 校验
├── DependencyInjection/
│   └── ModbusServiceCollectionExtensions.cs # DI 注册扩展
└── Communication.ModBus.csproj
```

---

## 运行时依赖

| 包名 | 最低版本 | 用途 |
|---|---|---|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 6.0.0 | DI 容器接口 |
| `Microsoft.Extensions.Logging.Abstractions` | 6.0.0 | 结构化日志 |
| `System.IO.Pipelines` | 6.0.0 | TCP 零拷贝 I/O |
| `System.IO.Ports` | 6.0.0 | RTU 串口通信 |

---

## 快速开始

### 安装

```bash
# NuGet
dotnet add package Communication.Modbus

# 或本地引用
dotnet add reference ../communication.modbus/Communication.ModBus.csproj
```

---

### 最简示例 — TCP（手动 new）

```csharp
using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Extensions;
using Communication.Modbus.TCP;

// 创建 TCP 客户端
using var tcp = new ModbusTCP(new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port    = 502
});

tcp.Connect();

// 读取 5 个线圈 (地址 0–4)
var coils = tcp.ReadCoils(slaveId: 1, start: 0, length: 5);
Console.WriteLine(coils.IsSuccess
    ? $"Coils: {string.Join(", ", coils.Data!)}"
    : $"Error: {coils.ErrorMessage}");

// 写入单个保持寄存器
var writeResult = tcp.WriteSingleRegister(slaveId: 1, start: 100, value: 1234);
Console.WriteLine(writeResult.IsSuccess ? "Write OK" : $"Error: {writeResult.ErrorMessage}");

tcp.Disconnect();
```

---

### 最简示例 — RTU（手动 new）

```csharp
using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Extensions;
using Communication.Modbus.RTU;
using System.IO.Ports;

// 创建 RTU 客户端
using var rtu = new ModbusRTU(new ModbusRTUConfig
{
    PortName = "COM3",
    BaudRate = 9600,
    Parity   = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
});

rtu.Connect();

// 异步读取 3 个保持寄存器 (地址 0–2)
var result = await rtu.ReadHoldingRegistersAsync(slaveId: 1, start: 0, length: 3);
Console.WriteLine(result.IsSuccess
    ? $"Registers: {string.Join(", ", result.Data!)}"
    : $"Error: {result.ErrorMessage}");

// 原子掩码写 (清除 bit 0–3, 设置 bit 4)
rtu.MaskWriteRegister(slaveId: 1, start: 200, andMask: 0xFFF0, orMask: 0x0010);
```

---

### 推荐方式 — DI + 工厂

```csharp
// Program.cs
using Communication.Modbus.DependencyInjection;
using Communication.Modbus.Factory;
using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
using Communication.Modbus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// 1. 构建 DI 容器
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

// 2. 注册 Modbus 服务（单例）
services.AddModbusFactory();

var provider = services.BuildServiceProvider();

// 3. 获取工厂
var factory = provider.GetRequiredService<IModbusFactory>();

// 4. 获取或创建 TCP 连接（key 保证幂等）
var tcp = factory.GetOrAdd("plc-1", new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port    = 502
});
tcp.Connect();

// 5. 执行操作
var coils = tcp.ReadCoils(slaveId: 1, start: 0, length: 5);
Console.WriteLine(coils.IsSuccess
    ? $"Coils: {string.Join(", ", coils.Data!)}"
    : $"Error: {coils.ErrorMessage}");

// 6. 容器释放时自动 Dispose 所有连接
```

---

### 多从站 RS-485（别名）

多个 RTU 从站共享一条 RS-485 总线时，使用 `RegisterAlias` 让多个逻辑名共享同一物理连接：

```csharp
var factory = provider.GetRequiredService<IModbusFactory>();

// 创建一条物理连接
var bus = factory.GetOrAdd("COM3-bus", new ModbusRTUConfig
{
    PortName = "COM3",
    BaudRate = 9600,
    Parity   = Parity.None
});
bus.Connect();

// 为每个从站注册别名
factory.RegisterAlias("slave-1", "COM3-bus");
factory.RegisterAlias("slave-2", "COM3-bus");

// 通过别名访问，指定不同的 SlaveId
var s1 = factory.GetRequired<IModbus>("slave-1");
var s2 = factory.GetRequired<IModbus>("slave-2");

s1.ReadHoldingRegisters(slaveId: 1, start: 0, length: 10);
s2.ReadHoldingRegisters(slaveId: 2, start: 0, length: 10);
```

---

### 全部功能码 API 一览

| 功能码 | 同步方法 | 异步方法 | 返回值 |
|---|---|---|---|
| 0x01 Read Coils | `ReadCoils(id, start, len)` | `ReadCoilsAsync(…, ct)` | `ModbusResult<bool[]>` |
| 0x02 Read Discrete Inputs | `ReadDiscreteInputs(id, start, len)` | `ReadDiscreteInputsAsync(…, ct)` | `ModbusResult<bool[]>` |
| 0x03 Read Holding Registers | `ReadHoldingRegisters(id, start, len)` | `ReadHoldingRegistersAsync(…, ct)` | `ModbusResult<ushort[]>` |
| 0x04 Read Input Registers | `ReadInputRegisters(id, start, len)` | `ReadInputRegistersAsync(…, ct)` | `ModbusResult<ushort[]>` |
| 0x05 Write Single Coil | `WriteSingleCoil(id, start, value)` | `WriteSingleCoilAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x06 Write Single Register | `WriteSingleRegister(id, start, value)` | `WriteSingleRegisterAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x0F Write Multiple Coils | `WriteMultipleCoils(id, start, values)` | `WriteMultipleCoilsAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x10 Write Multiple Registers | `WriteMultipleRegisters(id, start, values)` | `WriteMultipleRegistersAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x16 Mask Write Register | `MaskWriteRegister(id, start, and, or)` | `MaskWriteRegisterAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x17 Read/Write Multiple Registers | `ReadWriteMultipleRegisters(id, rStart, rLen, wStart, vals)` | `ReadWriteMultipleRegistersAsync(…, ct)` | `ModbusResult<ushort[]>` |

> 所有异步方法返回 `ValueTask<ModbusResult<T>>` · `ct` = `CancellationToken`

---

### 配置参数说明

#### ModbusTCPConfig

| 参数 | 默认值 | 说明 |
|---|---|---|
| `Address` | `"127.0.0.1"` | 从站 IP 地址 |
| `Port` | `502` | TCP 端口（支持 502 及 1024–65535） |
| `ConnectTimeout` | `2000` ms | 连接超时 |
| `ReadTimeOut` | `2000` ms | 读取超时 |
| `WriteTimeOut` | `2000` ms | 写入超时 |
| `RetryCount` | `3` | 重试次数 |
| `Reconnect` | `false` | 是否自动重连 |

#### ModbusRTUConfig

| 参数 | 默认值 | 说明 |
|---|---|---|
| `PortName` | `"COM20"` | 串口名称 |
| `BaudRate` | `9600` | 波特率 |
| `Parity` | `None` | 校验位 (None/Odd/Even/Mark/Space) |
| `DataBits` | `8` | 数据位 (5–8) |
| `StopBits` | `One` | 停止位 |
| `ReadTimeOut` | `2000` ms | 读取超时 |
| `WriteTimeOut` | `2000` ms | 写入超时 |
| `RetryCount` | `3` | 重试次数 |
| `IntervalTime` | `30` ms | 帧间等待间隔 |

---

### 运行时行为示例

成功日志片段（`ILogger` 输出）：

```text
info: ModbusFactory[0]
      GetOrAdd TCP: key=plc-1, address=192.168.1.100:502
dbug: ModbusTCP[0]
      [TX] [ModbusTCP] --> 00-01-00-00-00-06-01-01-00-00-00-05
dbug: ModbusTCP[0]
      [RX] [ModbusTCP] <-- 00-01-00-00-00-04-01-01-01-02 (+15 ms)
```

---

## 注意事项

### 并发模型

- **TCP:** 内部 `SemaphoreSlim(1,1)` 保证同一连接仅一个请求在飞行。多设备使用 `ModbusFactory` 创建多个实例。
- **RTU:** 串口为半双工通信，同样通过 `SemaphoreSlim` 串行化，避免帧冲突。

### 字节序

- Modbus 协议标准为大端序（Big-Endian）。库内部已处理 `ushort` ↔ `byte[]` 转换。
- 寄存器地址为 **0-based**（协议层自动转换）。

### 超时与重连

- `ReadTimeOut` / `WriteTimeOut` 默认 2000 ms。RTU 额外有 `IntervalTime` 帧间等待。
- `CheckConnection()` 基于 `Socket.Connected`，非心跳检测。长时间空闲后建议调用 `Connect()` 重建连接。

### 异常处理

- 输入校验失败抛出 `ModbusException`，附带标准 `ModbusErrorCode`。
- 通信失败返回 `ModbusResult<T>.Fail(errorMessage)`，**不抛异常**。
- 从站异常功能码（0x80+）自动识别并填充 `errorMessage`。

---

## 许可证

本项目基于 [MIT License](https://opensource.org/licenses/MIT) 开源。

Copyright (c) 2025 Junevy
