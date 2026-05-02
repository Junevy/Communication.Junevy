# Communication.ModBus

A modern, high-performance .NET library for Modbus TCP and RTU communication. Provides a
unified API for reading/writing coils, discrete inputs, holding registers, and input registers
with built-in connection pooling, DI integration, and full async support.

**Protocols:** Modbus TCP (via Ethernet) · Modbus RTU (via Serial Port)  
**Target Framework:** .NET 8.0+ · **Language:** C# 13 · **License:** MIT

---

## 目录结构

```
communication.modbus/
├── Core/                                  # 核心抽象与领域模型
│   ├── IModbus.cs                         # 统一通信接口（TCP/RTU）
│   ├── ModbusRequest.cs                   # 请求数据载体
│   ├── ModbusResult.cs                    # 泛型操作结果（IsSuccess / ErrorMessage）
│   ├── ModbusFunctionCode.cs              # 标准功能码枚举 (0x01–0x10)
│   ├── ModbusProtocolType.cs              # 协议类型 (TCP / RTU)
│   ├── ModbusException.cs                 # 统一异常类型（含 ModbusErrorCode）
│   └── ResponseParser.cs                  # 响应报文解析与校验
├── TCP/                                   # TCP 传输实现
│   ├── ModbusTCP.cs                       # Socket + PipeReader 通信引擎
│   └── ModbusTCPConfig.cs                 # TCP 配置（IP、Port、超时）
├── RTU/                                   # RTU 传输实现
│   ├── ModbusRTU.cs                       # SerialPort 通信引擎
│   └── ModbusRTUConfig.cs                 # RTU 配置（端口、波特率、校验位）
├── Extensions/                            # 面向用户的便捷 API
│   ├── ModbusExtensions.cs                # 8 个功能码的同步 + 异步扩展方法
│   ├── ByteHelper.cs                      # 大端序 / 十六进制 byte[] 辅助
│   └── LogExtentions.cs                   # 结构化日志扩展
├── Factory/                               # 多实例管理与 DI
│   ├── IModbusFactory.cs                  # 工厂接口（含 IDisposable/IAsyncDisposable）
│   └── ModbusFactory.cs                   # 工厂实现（ConcurrentDictionary 缓存池）
├── Utils/                                 # 底层工具
│   ├── ModbusHelper.cs                    # 帧构建、解析、校验
│   └── Crc16Helper.cs                     # CRC-16 计算与验证
├── DependencyInjection/                   # DI 扩展
│   └── ModbusServiceCollectionExtensions.cs  # AddModbusFactory() 注册方法
└── Communication.ModBus.csproj            # 项目文件
```

---

## 运行时依赖

| 包名 | 最低版本 | 用途 |
|---|---|---|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 6.0.0 | DI 容器接口（`IServiceCollection`） |
| `Microsoft.Extensions.Logging.Abstractions` | 6.0.0 | 结构化日志接口（`ILogger<T>`） |
| `System.IO.Pipelines` | 6.0.0 | TCP 高性能零拷贝 I/O（`PipeReader`） |
| `System.IO.Ports` | 6.0.0 | RTU 串口通信（`SerialPort`） |

> 测试项目依赖：`xUnit`、`Moq`、`Microsoft.Extensions.DependencyInjection`

---

## 快速开始

### 1. 安装

```bash
# NuGet（发布后）
dotnet add package Communication.Modbus

# 或本地引用
dotnet add reference ../communication.modbus/Communication.ModBus.csproj
```

### 2. 最简示例 — TCP 读取线圈

```csharp
using Communication.Modbus.Core;
using Communication.Modbus.Extensions;
using Communication.Modbus.TCP;

// 创建 TCP 客户端
using var tcp = new ModbusTCP(new ModbusTCPConfig
{
    Address = "192.168.1.100",
    Port   = 502
});

tcp.Connect();

// 同步读取线圈 0–4
var coils = tcp.ReadCoils(slaveId: 1, start: 0, length: 5);
if (coils.IsSuccess)
    Console.WriteLine($"Coils: {string.Join(", ", coils.Data!)}");
else
    Console.WriteLine($"Error: {coils.ErrorMessage}");

tcp.Disconnect();
```

### 3. 最简示例 — RTU 读取保持寄存器

```csharp
using var rtu = new ModbusRTU(new ModbusRTUConfig
{
    PortName = "COM3",
    BaudRate = 9600,
    Parity   = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
});

rtu.Connect();

// 异步读取 3 个保持寄存器，起始地址 0
var result = await rtu.ReadHoldingRegistersAsync(slaveId: 1, start: 0, length: 3);
Console.WriteLine(result.IsSuccess
    ? $"Registers: {string.Join(", ", result.Data!)}"
    : $"Error: {result.ErrorMessage}");
```

### 4. 使用工厂 + DI（推荐）

```csharp
// Program.cs
using Communication.Modbus.DependencyInjection;
using Communication.Modbus.Factory;

// 1) Get or set Container
var container = new ServiceCollection();

            // 1.1) Add logger if you want

            // Log.Logger = new LoggerConfiguration()
            //     .MinimumLevel.Debug()
            //     .WriteTo.File(
            //         path: "logs/app-.log",
            //         outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:1j}{NewLine}",
            //         rollingInterval: RollingInterval.Day,
            //         fileSizeLimitBytes: 5 * 1024 * 1024,
            //         rollOnFileSizeLimit: true,
            //         retainedFileCountLimit: 31,
            //         encoding: System.Text.Encoding.UTF8)
            //     .CreateLogger();

            // container.AddLogging( builder =>
            // {
            //     builder.ClearProviders();
            //     builder.AddSerilog();
            // });

// 2) Register factory
container.AddModbusFactory();   // 单例注册
var Provider = container.BuildServiceProvider();

// 3) Get factory from container
var factory = Provider.GetRequiredService<IModbusFactory>();

// 4) Get or add ModbusTCP instance from factory
var tcp = factory.GetOrAdd("test", new ModbusTCPConfig());

// 5) Use ModbusTCP instance
tcp.Connect();
var coils = tcp.ReadCoils(1, 0, 5);
if (coils.IsSuccess)
    Console.WriteLine($"Coils: {string.Join(", ", coils.Data!)}");
else
    Console.WriteLine($"Error: {coils.ErrorMessage}");
tcp.Disconnect();
// 容器释放时自动 Dispose 所有连接
```

---

### 全部功能码 API 一览

| 功能码 | 同步方法 | 异步方法 | 返回值 |
|---|---|---|---|
| 0x01 Read Coils | `ReadCoils(slaveId, start, length)` | `ReadCoilsAsync(…, ct)` | `ModbusResult<bool[]>` |
| 0x02 Read Discrete Inputs | `ReadDiscreteInputs(slaveId, start, length)` | `ReadDiscreteInputsAsync(…, ct)` | `ModbusResult<bool[]>` |
| 0x03 Read Holding Registers | `ReadHoldingRegisters(slaveId, start, length)` | `ReadHoldingRegistersAsync(…, ct)` | `ModbusResult<ushort[]>` |
| 0x04 Read Input Registers | `ReadInputRegisters(slaveId, start, length)` | `ReadInputRegistersAsync(…, ct)` | `ModbusResult<ushort[]>` |
| 0x05 Write Single Coil | `WriteSingleCoil(slaveId, start, value)` | `WriteSingleCoilAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x06 Write Single Register | `WriteSingleRegister(slaveId, start, value)` | `WriteSingleRegisterAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x0F Write Multiple Coils | `WriteMultipleCoils(slaveId, start, values)` | `WriteMultipleCoilsAsync(…, ct)` | `ModbusResult<byte[]>` |
| 0x10 Write Multiple Registers | `WriteMultipleRegisters(slaveId, start, values)` | `WriteMultipleRegistersAsync(…, ct)` | `ModbusResult<byte[]>` |

> 同步方法返回 `ModbusResult<T>` · 异步方法返回 `ValueTask<ModbusResult<T>>` · `ct` = `CancellationToken`

---

### 配置参数说明

#### ModbusTCPConfig

| 参数 | 默认值 | 说明 |
|---|---|---|
| `Address` | `"127.0.0.1"` | 从站 IP 地址 |
| `Port` | `502` | TCP 端口（仅支持 1024–65535） |
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
info: ModbusRTU[0]
      [Request] Build Execute Tx: {"SlaveId":1,"FunctionCode":"ReadCoils","Start":0,"Length":5}
dbug: ModbusRTU[0]
      [TX] [192.168.1.100] --> 00 01 00 00 00 06 01 01 00 00 00 05
dbug: ModbusRTU[0]
      [RX] [192.168.1.100] <-- 00 01 00 00 00 04 01 01 01 02 (+15 ms)
```

---

## 注意事项

### 网络与串口并发

- **TCP:** 内部使用 `SemaphoreSlim(1, 1)` 保证同一连接上一次仅有一个请求在飞行。多个设备请使用 `ModbusFactory` 创建多个实例。
- **RTU:** 串口天然为单线程半双工通信。同样通过 `SemaphoreSlim` 串行化，避免帧冲突。

### 字节序（Endianness）

- Modbus 协议标准为大端序（Big-Endian）。本库内部已处理 `ushort` 与 `byte[]` 的转换，用户无需关心字节序。
- 寄存器地址为 **0-based**（协议层自动转为 1-based）。

### 超时与重试

- `ReadTimeOut` / `WriteTimeOut` 默认 2000 ms。RTU 额外有 `IntervalTime` 用于帧间等待。
- `CheckConnection()` 基于 `Socket.Connected` 判断，非心跳检测，建议在长时间空闲后自行调用 `Connect()` 重建连接。

### 异常处理

- 输入校验失败抛出 `ModbusException`，附带标准 `ModbusErrorCode`。
- 通信失败返回 `ModbusResult<T>.Fail(errorMessage)`，**不抛异常**。
- 从站异常功能码（0x80+）自动识别并填充 `errorMessage`。

---

## 许可证

本项目基于 [MIT License](https://opensource.org/licenses/MIT) 开源。

[MIT](LICENSE)

Copyright (c) 2025 Junevy
