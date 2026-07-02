using Communication.Modbus.Core.Interfaces;
using Communication.Modbus.Core.Models;
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
            ModbusFunctionCode funcCode, Func<byte[], int, T[]> parser)
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

                // 异常验证
                if ((byte)(slice[1] & 0x80) >= 0x80)
                    return ModbusResult<T[]>.Fail($"Exception code: {slice[1]}");

                var parsed = parser(slice.ToArray(), length);
                return ModbusResult<T[]>.Success(parsed);
            }

            return ModbusResult<T[]>.Fail(result.ErrorMessage ?? $" Check sent tx please.");
        }

        /// <summary>
        /// 通用的异步读取请求
        /// </summary>
        private static async ValueTask<ModbusResult<T[]>> ExecuteReadRequestAsync<T>(IModbus modBus, byte slaveId, ushort start, ushort length,
            ModbusFunctionCode funcCode, Func<byte[], int, T[]> parser, CancellationToken cancellationToken = default)
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
                // 异常验证
                if ((byte)(slice[1] & 0x80) >= 0x80)
                    return ModbusResult<T[]>.Fail($"Exception code: {slice[1]}");

                var parsed = parser(slice.ToArray(), length);
                return ModbusResult<T[]>.Success(parsed);
            }

            return ModbusResult<T[]>.Fail(result.ErrorMessage ?? $" Get result failed, check sent request please.");
        }

        /// <summary>
        /// 通用的同步写入请求
        /// </summary>
        private static ModbusResult<byte[]> ExecuteWriteRequest(IModbus modBus, byte slaveId, ushort start, ushort length, ModbusFunctionCode funcCode, byte[] data)
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
            var pduOffset = modBus.ProtocolType == ModbusProtocolType.TCP ? 6 : 0;
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                if ((byte)(result.Data[pduOffset + 1] & 0x80) >= 0x80)  // 异常码
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Exception code: {result.Data[pduOffset + 1]}";
                    return result;
                }
                return result;
            }

            return result;
        }

        /// <summary>
        /// 通用的异步写入请求
        /// </summary>
        private static async ValueTask<ModbusResult<byte[]>> ExecuteWriteRequestAsync(IModbus modBus, byte slaveId, ushort start, ushort length, ModbusFunctionCode funcCode,
            byte[] data, CancellationToken cancellationToken = default)
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
            var pduOffset = modBus.ProtocolType == ModbusProtocolType.TCP ? 6 : 0;
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                if ((byte)(result.Data[pduOffset + 1] & 0x80) >= 0x80)  // 异常码
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Exception code: {result.Data[pduOffset + 1]}";
                    return result;
                }
                return result;
            }

            return result;
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
                ModbusHelper.ParseCoils);
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
                ModbusHelper.ParseCoils, cancellationToken);
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
                ModbusHelper.ParseCoils);
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
                ModbusHelper.ParseCoils, cancellationToken);
        }

        /// <summary>
        /// 同步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        public static ModbusResult<ushort[]> ReadHoldingRegisters(this IModbus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 125)
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadHoldingRegisters] The length must be between 1 and 125.");

            return ExecuteReadRequest(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadHoldingRegisters,
                ModbusHelper.ParseRegisters);
        }

        /// <summary>
        /// 异步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        public static async ValueTask<ModbusResult<ushort[]>> ReadHoldingRegistersAsync(this IModbus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 125)
                throw new ModbusException(ModbusErrorCode.InvalidValue, " [ReadHoldingRegistersAsync] The length must be between 1 and 125.");

            return await ExecuteReadRequestAsync(
                modBus, slaveId, start, length, ModbusFunctionCode.ReadHoldingRegisters,
                ModbusHelper.ParseRegisters, cancellationToken);
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
                ModbusHelper.ParseRegisters);
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
                ModbusHelper.ParseRegisters, cancellationToken);
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

            return ExecuteWriteRequest(modBus, slaveId, start, 1, ModbusFunctionCode.WriteCoil, data);
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

            return await ExecuteWriteRequestAsync(modBus, slaveId, start, 1, ModbusFunctionCode.WriteCoil, data, cancellationToken);
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

            return ExecuteWriteRequest(modBus, slaveId, start, 1, ModbusFunctionCode.WriteHoldingRegister, data);
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
                modBus, slaveId, start, 1, ModbusFunctionCode.WriteHoldingRegister, data, cancellationToken);
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
        public static ModbusResult<byte[]> WriteMultipleCoils(this IModbus modBus, byte slaveId, ushort start, bool[] values)
        {
            if (values == null || values.Length == 0 || values.Length > 1968)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [WriteMultipleCoils] The coil quantity must be between 1 and 1968.");

            int byteCount = (values.Length + 7) / 8;
            byte[] data = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    data[i / 8] |= (byte)(1 << (i % 8));
            }

            return ExecuteWriteRequest(
                modBus, slaveId, start, (ushort)values.Length, ModbusFunctionCode.WriteMultipleCoils, data);
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
        public static async ValueTask<ModbusResult<byte[]>> WriteMultipleCoilsAsync(this IModbus modBus, byte slaveId, ushort start, bool[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length <= 0 || values.Length > 1968)
                throw new ModbusException(ModbusErrorCode.InvalidData,
                    " [WriteMultipleCoilsAsync] The values array must match the specified length.");

            int byteCount = (values.Length + 7) / 8;
            byte[] data = new byte[byteCount];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i])
                    data[i / 8] |= (byte)(1 << (i % 8));
            }

            return await ExecuteWriteRequestAsync(
                modBus, slaveId, start, (ushort)values.Length,
                ModbusFunctionCode.WriteMultipleCoils, data, cancellationToken);
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
                modBus, slaveId, start, (ushort)values.Length, ModbusFunctionCode.WriteMultipleHoldingRegisters, data);
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
                ModbusFunctionCode.WriteMultipleHoldingRegisters, data, cancellationToken);
        }

        /// <summary>
        /// Mask Write Register (0x16) — atomically applies AND mask then OR mask to a single holding register.
        /// Result = (CurrentValue &amp; andMask) | orMask.
        /// </summary>
        /// <param name="modBus">IModbus instance.</param>
        /// <param name="slaveId">Slave ID.</param>
        /// <param name="start">Register address.</param>
        /// <param name="andMask">AND mask (bits cleared where mask is 0).</param>
        /// <param name="orMask">OR mask (bits set where mask is 1).</param>
        public static ModbusResult<byte[]> MaskWriteRegister(this IModbus modBus, byte slaveId, ushort start, ushort andMask, ushort orMask)
        {
            byte[] data =
            [
                .. andMask.ToBigEndian(),
                .. orMask.ToBigEndian(),
            ];

            return ExecuteWriteRequest(modBus, slaveId, start, 1, ModbusFunctionCode.MaskWriteRegister, data);
        }

        /// <summary>
        /// Mask Write Register (0x16) — async version.
        /// </summary>
        public static async ValueTask<ModbusResult<byte[]>> MaskWriteRegisterAsync(this IModbus modBus, byte slaveId, ushort start,
            ushort andMask, ushort orMask, CancellationToken cancellationToken = default)
        {
            byte[] data =
            [
                .. andMask.ToBigEndian(),
                .. orMask.ToBigEndian(),
            ];

            return await ExecuteWriteRequestAsync(modBus, slaveId, start, 1, ModbusFunctionCode.MaskWriteRegister, data, cancellationToken);
        }

        /// <summary>
        /// Read/Write Multiple Registers (0x17) — writes values to one register range and reads from another
        /// in a single atomic transaction.
        /// </summary>
        /// <param name="modBus">IModbus instance.</param>
        /// <param name="slaveId">Slave ID.</param>
        /// <param name="readStart">Starting address to read from.</param>
        /// <param name="readLength">Number of registers to read (1..125).</param>
        /// <param name="writeStart">Starting address to write to.</param>
        /// <param name="writeValues">Register values to write (1..121).</param>
        public static ModbusResult<ushort[]> ReadWriteMultipleRegisters(this IModbus modBus, byte slaveId,
            ushort readStart, ushort readLength, ushort writeStart, ushort[] writeValues)
        {
            if (readLength == 0 || readLength > 125)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [ReadWriteMultipleRegisters] The read quantity must be between 1 and 125.");
            if (writeValues == null || writeValues.Length == 0 || writeValues.Length > 121)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [ReadWriteMultipleRegisters] The write quantity must be between 1 and 121.");

            byte[] writeData = writeValues.ToBigEndianByteArray();
            byte writeByteCount = (byte)(writeValues.Length * 2);

            // Encode all parameters into Data for the custom frame builder
            byte[] data =
            [
                .. readStart.ToBigEndian(),
                .. readLength.ToBigEndian(),
                .. writeStart.ToBigEndian(),
                .. ((ushort)writeValues.Length).ToBigEndian(),
                writeByteCount,
                .. writeData,
            ];

            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModbusFunctionCode.ReadWriteMultipleRegisters,
                Start = readStart,
                Length = readLength,
                Data = data
            };

            var result = modBus.Request(tx);
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                var pduOffset = modBus.ProtocolType == ModbusProtocolType.TCP ? 6 : 0;
                var slice = result.Data.AsMemory().Span;
                if ((byte)(slice[pduOffset + 1] & 0x80) >= 0x80)
                    return ModbusResult<ushort[]>.Fail($"Exception code: {slice[pduOffset + 1]}");

                var parsed = ModbusHelper.ParseRegisters(slice.ToArray(), readLength);
                return ModbusResult<ushort[]>.Success(parsed);
            }

            return ModbusResult<ushort[]>.Fail(result.ErrorMessage ?? " [ReadWriteMultipleRegisters] Request failed.");
        }

        /// <summary>
        /// Read/Write Multiple Registers (0x17) — async version.
        /// </summary>
        public static async ValueTask<ModbusResult<ushort[]>> ReadWriteMultipleRegistersAsync(this IModbus modBus, byte slaveId,
            ushort readStart, ushort readLength, ushort writeStart, ushort[] writeValues,
            CancellationToken cancellationToken = default)
        {
            if (readLength == 0 || readLength > 125)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [ReadWriteMultipleRegistersAsync] The read quantity must be between 1 and 125.");
            if (writeValues == null || writeValues.Length == 0 || writeValues.Length > 121)
                throw new ModbusException(ModbusErrorCode.InvalidQuantity,
                    " [ReadWriteMultipleRegistersAsync] The write quantity must be between 1 and 121.");

            byte[] writeData = writeValues.ToBigEndianByteArray();
            byte writeByteCount = (byte)(writeValues.Length * 2);

            byte[] data =
            [
                .. readStart.ToBigEndian(),
                .. readLength.ToBigEndian(),
                .. writeStart.ToBigEndian(),
                .. ((ushort)writeValues.Length).ToBigEndian(),
                writeByteCount,
                .. writeData,
            ];

            var tx = new ModbusRequest
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModbusFunctionCode.ReadWriteMultipleRegisters,
                Start = readStart,
                Length = readLength,
                Data = data
            };

            var result = await modBus.RequestAsync(tx, cancellationToken);
            if (result.IsSuccess && result.Data?.Length > 0)
            {
                var pduOffset = modBus.ProtocolType == ModbusProtocolType.TCP ? 6 : 0;
                var slice = result.Data.AsMemory().Span;
                if ((byte)(slice[pduOffset + 1] & 0x80) >= 0x80)
                    return ModbusResult<ushort[]>.Fail($"Exception code: {slice[pduOffset + 1]}");

                var parsed = ModbusHelper.ParseRegisters(slice.ToArray(), readLength);
                return ModbusResult<ushort[]>.Success(parsed);
            }

            return ModbusResult<ushort[]>.Fail(result.ErrorMessage ?? " [ReadWriteMultipleRegistersAsync] Request failed.");
        }
    }
}
