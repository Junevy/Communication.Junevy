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
        /// 通用的同步读取请求
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
        /// 通用的异步读取请求
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
        /// 通用的同步写入请求
        /// </summary>
        private static ModbusResult<byte[]> ExecuteWriteRequest(IModbus modBus, byte slaveId, ushort start, ushort length, ModbusFunctionCode funcCode, byte[] data, string errorPrefix)
        {
            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = funcCode,
                Start = start,
                Length = length,
                Data = data
            };

            var result = modBus.Request(tx);
            if (result.IsSuccess && result.Data?.Length > 0)
                return ModbusResult<byte[]>.Success(result.Data);

            return ModbusResult<byte[]>.Fail(result.ErrorMessage ?? $" {errorPrefix} Check sent tx please.");
        }

        /// <summary>
        /// 通用的异步写入请求
        /// </summary>
        private static async ValueTask<ModbusResult<byte[]>> ExecuteWriteRequestAsync(IModbus modBus, byte slaveId, ushort start, ushort length, ModbusFunctionCode funcCode, 
            byte[] data, string errorPrefix, CancellationToken cancellationToken = default)
        {
            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = funcCode,
                Start = start,
                Length = length,
                Data = data
            };

            var result = await modBus.RequestAsync(tx, cancellationToken);
            if (result.IsSuccess && result.Data?.Length > 0)
                return ModbusResult<byte[]>.Success(result.Data);

            return ModbusResult<byte[]>.Fail(result.ErrorMessage ?? $" {errorPrefix} Get result failed, check sent tx please.");
        }


        /// <summary>
        /// 同步读取线圈 (0x01 - Read Coils)
        /// </summary>
        public static ModbusResult<bool[]> ReadCoils(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 2000)
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadCoils] The length must be between 1 and 2000.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadCoilsAsync] The length must be between 1 and 2000.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadDiscreteInputs] The length must be between 1 and 2000.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadDiscreteInputsAsync] The length must be between 1 and 2000.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadHoldingRegisters] The length must be between 1 and 125.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadHoldingRegistersAsync] The length must be between 1 and 125.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadInputRegisters] The length must be between 1 and 125.");

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
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadInputRegistersAsync] The length must be between 1 and 125.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadInputRegisters,
                ModbusHelper.ParseRegisters,
                " [ReadInputRegistersAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步写单个线圈 (0x05 - Write Single Coil)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">线圈地址</param>
        /// <param name="value">线圈值 (true: ON 0xFF00, false: OFF 0x0000)</param>
        /// <returns>写入操作结果对象</returns>
        public static ModbusResult<byte[]> WriteSingleCoil(this IModbus modBus, byte slaveId, ushort start, bool value)
        {
            byte[] data = [(byte)(value ? 0xFF : 0x00), 0x00];

            return ExecuteWriteRequest(
                modBus, slaveId, start, 1,
                ModbusFunctionCode.WriteCoil, data,
                " [WriteSingleCoil] ");
        }

        /// <summary>
        /// 异步写单个线圈 (0x05 - Write Single Coil)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">线圈地址</param>
        /// <param name="value">线圈值 (true: ON 0xFF00, false: OFF 0x0000)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入操作结果对象</returns>
        public static async ValueTask<ModbusResult<byte[]>> WriteSingleCoilAsync(this IModbus modBus, byte slaveId, ushort start, bool value, CancellationToken cancellationToken = default)
        {
            byte[] data = [(byte)(value ? 0xFF : 0x00), 0x00];

            return await ExecuteWriteRequestAsync(
                modBus, slaveId, start, 1,
                ModbusFunctionCode.WriteCoil, data,
                " [WriteSingleCoilAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步写单个保持寄存器 (0x06 - Write Single Holding Register)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">寄存器地址</param>
        /// <param name="value">寄存器值 (16-bit unsigned)</param>
        /// <returns>写入操作结果对象</returns>
        public static ModbusResult<byte[]> WriteSingleRegister(this IModbus modBus, byte slaveId, ushort start, ushort value)
        {
            byte[] data = value.ToBigEndian();

            return ExecuteWriteRequest(
                modBus, slaveId, start, 1,
                ModbusFunctionCode.WriteHodingRegister, data,
                " [WriteSingleRegister] ");
        }

        /// <summary>
        /// 异步写单个保持寄存器 (0x06 - Write Single Holding Register)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">寄存器地址</param>
        /// <param name="value">寄存器值 (16-bit unsigned)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入操作结果对象</returns>
        public static async ValueTask<ModbusResult<byte[]>> WriteSingleRegisterAsync(this IModbus modBus, byte slaveId, ushort start, ushort value, CancellationToken cancellationToken = default)
        {
            byte[] data = value.ToBigEndian();

            return await ExecuteWriteRequestAsync(
                modBus, slaveId, start, 1,
                ModbusFunctionCode.WriteHodingRegister, data,
                " [WriteSingleRegisterAsync] ",
                cancellationToken);
        }


        /// <summary>
        /// 同步写多个线圈 (0x0F - Write Multiple Coils)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始线圈地址</param>
        /// <param name="length">线圈数量 (1..1968)</param>
        /// <param name="values">线圈值数组</param>
        /// <returns>写入操作结果对象</returns>
        /// <exception cref="ModbusException">地址/数量/值非法</exception>
        public static ModbusResult<byte[]> WriteMultipleCoils(this IModbus modBus, byte slaveId, ushort start, ushort length, bool[] values)
        {
            if (length == 0 || length > 1968)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [WriteMultipleCoils] The coil quantity must be between 1 and 1968.");

            if (values == null || values.Length != length)
                throw new ModbusException(ModbusErrorCode.InvalidData,
                    " [WriteMultipleCoils] The values array must match the specified length.");

            int byteCount = (length + 7) / 8;
            byte[] data = new byte[byteCount];
            for (int i = 0; i < length; i++)
            {
                if (values[i])
                    data[i / 8] |= (byte)(1 << (i % 8));
            }

            return ExecuteWriteRequest(
                modBus, slaveId, start, length,
                ModbusFunctionCode.WriteMultiCoils, data,
                " [WriteMultipleCoils] ");
        }

        /// <summary>
        /// 异步写多个线圈 (0x0F - Write Multiple Coils)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始线圈地址</param>
        /// <param name="length">线圈数量 (1..1968)</param>
        /// <param name="values">线圈值数组</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入操作结果对象</returns>
        /// <exception cref="ModbusException">地址/数量/值非法</exception>
        public static async ValueTask<ModbusResult<byte[]>> WriteMultipleCoilsAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, bool[] values, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 1968)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [WriteMultipleCoilsAsync] The coil quantity must be between 1 and 1968.");

            if (values == null || values.Length != length)
                throw new ModbusException(ModbusErrorCode.InvalidData,
                    " [WriteMultipleCoilsAsync] The values array must match the specified length.");

            int byteCount = (length + 7) / 8;
            byte[] data = new byte[byteCount];
            for (int i = 0; i < length; i++)
            {
                if (values[i])
                    data[i / 8] |= (byte)(1 << (i % 8));
            }

            return await ExecuteWriteRequestAsync(
                modBus, slaveId, start, length,
                ModbusFunctionCode.WriteMultiCoils, data,
                " [WriteMultipleCoilsAsync] ",
                cancellationToken);
        }

        /// <summary>
        /// 同步写多个保持寄存器 (0x10 - Write Multiple Holding Registers)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始寄存器地址</param>
        /// <param name="values">寄存器值数组 (每个元素 16-bit unsigned)</param>
        /// <returns>写入操作结果对象</returns>
        /// <exception cref="ModbusException">地址/数量/值非法</exception>
        public static ModbusResult<byte[]> WriteMultipleRegisters(this IModbus modBus, byte slaveId, ushort start, ushort[] values)
        {
            if (values == null || values.Length == 0 || values.Length > 123)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [WriteMultipleRegisters] The register quantity must be between 1 and 123.");

            byte[] data = values.ToBigEndianByteArray();

            return ExecuteWriteRequest(
                modBus, slaveId, start, (ushort)values.Length,
                ModbusFunctionCode.WriteMultiHodingRegisters, data,
                " [WriteMultipleRegisters] ");
        }

        /// <summary>
        /// 异步写多个保持寄存器 (0x10 - Write Multiple Holding Registers)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始寄存器地址</param>
        /// <param name="values">寄存器值数组 (每个元素 16-bit unsigned)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>写入操作结果对象</returns>
        /// <exception cref="ModbusException">地址/数量/值非法</exception>
        public static async ValueTask<ModbusResult<byte[]>> WriteMultipleRegistersAsync(this IModbus modBus, byte slaveId, ushort start, ushort[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0 || values.Length > 123)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [WriteMultipleRegistersAsync] The register quantity must be between 1 and 123.");

            byte[] data = values.ToBigEndianByteArray();

            return await ExecuteWriteRequestAsync(
                modBus, slaveId, start, (ushort)values.Length,
                ModbusFunctionCode.WriteMultiHodingRegisters, data,
                " [WriteMultipleRegistersAsync] ",
                cancellationToken);
        }
    }
}
