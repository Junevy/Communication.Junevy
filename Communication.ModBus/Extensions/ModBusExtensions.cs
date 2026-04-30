using Communication.Modbus.Core;
using Communication.Modbus.Utils;

namespace Communication.Modbus.Extensions
{
    /// <summary>
    /// 提供 IModBus 的扩展方法，方便用户直接调用各功能码，而无需手动构建 Tx 对象。
    /// </summary>
    public static class ModbusExtensions
    {
        /// <summary>
        /// 通用的同步读取请求（线圈或寄存器）
        /// </summary>
        private static ModbusResult<T[]> ExecuteReadRequest<T>(IModbus modBus, byte slaveId, ushort start, ushort length,
            ModbusFunctionCode funcCode, Func<byte[], int, T[]> parser, string errorPrefix = "ExecuteReadRequest")
        {
            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = funcCode,
                Start = start,
                Length = length
            };

            var result = modBus.Request(tx);
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                ReadOnlySpan<byte> slice = modBus.ProtocolType == ModbusProtocolType.TCP ? result.Data.AsMemory().Span[6..] : result.Data.AsMemory().Span;
                var parsed = parser(slice.ToArray(), length);
                return ModbusResult<T[]>.Success(parsed);
            }

            return ModbusResult<T[]>.Fail(result.ErrorMessage ?? $" {errorPrefix} Check sent tx please.");
        }

        /// <summary>
        /// 通用的异步读取请求（线圈或寄存器）
        /// </summary>
        private static async ValueTask<ModbusResult<T[]>> ExecuteReadRequestAsync<T>(IModbus modBus, byte slaveId, ushort start, ushort length, 
            ModbusFunctionCode funcCode, Func<byte[], int, T[]> parser, string errorPrefix = "ExecuteReadRequestAsync", CancellationToken cancellationToken = default)
        {
            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = funcCode,
                Start = start,
                Length = length
            };

            var result = await modBus.RequestAsync(tx, cancellationToken);
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                ReadOnlySpan<byte> slice = modBus.ProtocolType == ModbusProtocolType.TCP ? result.Data.AsMemory().Span[6..] : result.Data.AsMemory().Span;
                var parsed = parser(slice.ToArray(), length);
                return ModbusResult<T[]>.Success(parsed);
            }

            return ModbusResult<T[]>.Fail(result.ErrorMessage ?? $" {errorPrefix} Get result failed, check sent tx please.");
        }

        /// <summary>
        /// 同步读取线圈 (0x01 - Read Coils)
        /// </summary>
        public static ModbusResult<bool[]> ReadCoils(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            return ExecuteReadRequest(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadCoils,
                ModbusHelper.ParseCoils,
                " [ReadCoils] ");
        }

        /// <summary>
        /// 异步读取线圈 (0x01 - Read Coils)
        /// </summary>
        public static async ValueTask<ModbusResult<bool[]>> ReadCoilsAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadCoils,
                ModbusHelper.ParseCoils,
                " [ReadCoilsAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步读取离散输入 (0x02 - Read Input Registers)
        /// </summary>
        public static ModbusResult<bool[]> ReadDiscreteInputs(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            return ExecuteReadRequest(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadDiscreteInputs,
                ModbusHelper.ParseCoils,
                " [ReadDiscreteInputs] ");
        }

        /// <summary>
        /// 异步读取离散输入 (0x02 - Read Input Registers)
        /// </summary>
        public static async ValueTask<ModbusResult<bool[]>> ReadDiscreteInputsAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadDiscreteInputs,
                ModbusHelper.ParseCoils,
                " [ReadDiscreteInputsAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        public static ModbusResult<ushort[]> ReadHoldingRegisters(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), " [ReadHoldingRegisters] The length must be between 1 and 125.");

            return ExecuteReadRequest(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadHodingRegisters,
                ModbusHelper.ParseRegisters,
                " [ReadHoldingRegisters] ");
        }

        /// <summary>
        /// 异步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        public static async ValueTask<ModbusResult<ushort[]>> ReadHoldingRegistersAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), " [ReadHoldingRegistersAsync] The length must be between 1 and 125.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadHodingRegisters,
                ModbusHelper.ParseRegisters, 
                " [ReadHoldingRegistersAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步读取输入寄存器 (0x04 - Read Input Registers)
        /// </summary>
        public static ModbusResult<ushort[]> ReadInputRegisters(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), " [ReadInputRegisters] The length must be between 1 and 125.");

            return ExecuteReadRequest(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadInputRegisters,
                ModbusHelper.ParseRegisters,
                " [ReadInputRegisters] ");
        }

        /// <summary>
        /// 异步读取输入寄存器 (0x04 - Read Input Registers)
        /// </summary>
        public static async ValueTask<ModbusResult<ushort[]>> ReadInputRegistersAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), " [ReadInputRegistersAsync] The length must be between 1 and 125.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadInputRegisters,
                ModbusHelper.ParseRegisters, 
                " [ReadInputRegistersAsync] ",
                cancellationToken);
        }

    }
}


//        /// <summary>
//        /// 同步写单线圈 (0x05 - Write Single Coil)
//        /// </summary>
//        public static bool WriteSingleCoil(this IModbus modBus, byte slaveId, ushort start, bool value)
//        {
//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteCoil,
//                Start = start,
//                Length = 1,
//                Data = [(byte)(value ? 0xFF : 0x00), 0x00]
//            };

//            return modBus.Request(tx).IsSuccess;
//        }

//        /// <summary>
//        /// 异步写单线圈 (0x05 - Write Single Coil)
//        /// </summary>
//        /// <param name="modBus">IModBus 实例</param>
//        /// <param name="slaveId">从站 ID</param>
//        /// <param name="start">起始地址</param>
//        /// <param name="value">要写入的布尔值 (true: 0xFF00, false: 0x0000)</param>
//        /// <param name="cancellationToken">取消令牌</param>
//        /// <returns>响应结果</returns>
//        public static async Task<bool> WriteSingleCoilAsync(this IModbus modBus, byte slaveId, ushort start, bool value, CancellationToken cancellationToken = default)
//        {
//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteCoil,
//                Start = start,
//                Length = 1,
//                Data = [(byte)(value ? 0xFF : 0x00), 0x00]
//            };

//            var result = await modBus.RequestAsync(tx, cancellationToken);
//            return result.IsSuccess;
//        }

//        /// <summary>
//        /// 同步写单保持寄存器 (0x06 - Write Single Holding Register)
//        /// </summary>
//        public static bool WriteSingleRegister(this IModbus modBus, byte slaveId, ushort start, ushort value)
//        {
//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteHodingRegister,
//                Start = start,
//                Length = 1,
//                Data = BitExtentions.ToBytesByBigEndian(value)
//            };

//            return modBus.Request(tx).IsSuccess;
//        }

//        /// <summary>
//        /// 异步写单保持寄存器 (0x06 - Write Single Holding Register)
//        /// </summary>
//        /// <param name="modBus">IModBus 实例</param>
//        /// <param name="slaveId">从站 ID</param>
//        /// <param name="start">起始地址</param>
//        /// <param name="value">要写入的寄存器值</param>
//        /// <param name="cancellationToken">取消令牌</param>
//        /// <returns>响应结果</returns>
//        public static async Task<bool> WriteSingleRegisterAsync(this IModbus modBus, byte slaveId, ushort start, ushort value, CancellationToken cancellationToken = default)
//        {
//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteHodingRegister,
//                Start = start,
//                Length = 1,
//                Data = BitExtentions.ToBytesByBigEndian(value)
//            };

//            var result = await modBus.RequestAsync(tx, cancellationToken);
//            return result.IsSuccess;
//        }

//        /// <summary>
//        /// 同步写多线圈 (0x0F - Write Multiple Coils)
//        /// </summary>
//        public static bool WriteMultipleCoils(this IModbus modBus, byte slaveId, ushort start, ushort length, byte[] data)
//        {
//            if (length == 0 || length > 1968)
//                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 1968.");
//            if (data == null || data.Length == 0)
//                throw new ArgumentException("The data cannot be null or empty.", nameof(data));

//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteMultiCoils,
//                Start = start,
//                Length = length,
//                Data = data
//            };

//            return modBus.Request(tx).IsSuccess;
//        }

//        /// <summary>
//        /// 异步写多线圈 (0x0F - Write Multiple Coils)
//        /// </summary>
//        /// <param name="modBus">IModBus 实例</param>
//        /// <param name="slaveId">从站 ID</param>
//        /// <param name="start">起始地址</param>
//        /// <param name="length">写入数量（线圈数）</param>
//        /// <param name="data">打包后的线圈数据</param>
//        /// <param name="cancellationToken">取消令牌</param>
//        /// <returns>响应结果</returns>
//        public static async Task<bool> WriteMultipleCoilsAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, byte[] data, CancellationToken cancellationToken = default)
//        {
//            if (length == 0 || length > 1968)
//                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 1968.");
//            if (data == null || data.Length == 0)
//                throw new ArgumentException("The data cannot be null or empty.", nameof(data));

//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteMultiCoils,
//                Start = start,
//                Length = length,
//                Data = data
//            };

//            var result = await modBus.RequestAsync(tx, cancellationToken);
//            return result.IsSuccess;
//        }

//        /// <summary>
//        /// 同步写多保持寄存器 (0x10 - Write Multiple Holding Registers)
//        /// </summary>
//        public static bool WriteMultipleRegisters(this IModbus modBus, byte slaveId, ushort start, ushort[] values)
//        {
//            if (values == null || values.Length == 0)
//                throw new ArgumentException("The values cannot be null or empty.", nameof(values));
//            if (values.Length > 123)
//                throw new ArgumentOutOfRangeException(nameof(values), "The length must be between 1 and 123.");

//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteMultiHodingRegisters,
//                Start = start,
//                Length = (ushort)values.Length,
//                Data = values.ToByteArrayBigEndian()
//            };

//            return modBus.Request(tx).IsSuccess;
//        }

//        /// <summary>
//        /// 异步写多保持寄存器 (0x10 - Write Multiple Holding Registers)
//        /// </summary>
//        /// <param name="modBus">IModBus 实例</param>
//        /// <param name="slaveId">从站 ID</param>
//        /// <param name="start">起始地址</param>
//        /// <param name="values">要写入的寄存器值数组</param>
//        /// <param name="cancellationToken">取消令牌</param>
//        /// <returns>响应结果</returns>
//        public static async Task<bool> WriteMultipleRegistersAsync(this IModbus modBus, byte slaveId, ushort start, ushort[] values, CancellationToken cancellationToken = default)
//        {
//            if (values == null || values.Length == 0)
//                throw new ArgumentException("The values cannot be null or empty.", nameof(values));
//            if (values.Length > 123)
//                throw new ArgumentOutOfRangeException(nameof(values), "The length must be between 1 and 123.");

//            var tx = new ModbusTx
//            {
//                ProtocolType = modBus.ProtocolType,
//                SlaveId = slaveId,
//                FunctionCode = ModbusFunctionCode.WriteMultiHodingRegisters,
//                Start = start,
//                Length = (ushort)values.Length,
//                Data = values.ToByteArrayBigEndian()
//            };

//            var result = await modBus.RequestAsync(tx, cancellationToken);
//            return result.IsSuccess;
//        }
//    }
//}
