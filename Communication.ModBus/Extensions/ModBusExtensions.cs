using Communication.ModBus.Core;
using Communication.ModBus.Utils;

namespace Communication.ModBus.Extensions
{
    /// <summary>
    /// 提供 IModBus 的扩展方法，方便用户直接调用各功能码，而无需手动构建 Tx 对象。
    /// </summary>
    public static class ModBusExtensions
    {
        /// <summary>
        /// 同步读取线圈 (0x01 - Read Coils)
        /// </summary>
        public static Rx<byte[]> ReadCoils(this IModBus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadCoils,
                Start = start,
                Length = length
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步读取线圈 (0x01 - Read Coils)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取数量（线圈数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> ReadCoilsAsync(this IModBus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadCoils,
                Start = start,
                Length = length
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步读取离散输入 (0x02 - Read Discrete Inputs)
        /// </summary>
        public static Rx<byte[]> ReadDiscreteInputs(this IModBus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadDiscreteInputs,
                Start = start,
                Length = length
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步读取离散输入 (0x02 - Read Discrete Inputs)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取数量（离散输入数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> ReadDiscreteInputsAsync(this IModBus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 2000)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 2000.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadDiscreteInputs,
                Start = start,
                Length = length
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        public static Rx<byte[]> ReadHoldingRegisters(this IModBus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 125.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadHodingRegister,
                Start = start,
                Length = length
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步读取保持寄存器 (0x03 - Read Holding Registers)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取数量（寄存器数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> ReadHoldingRegistersAsync(this IModBus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 125.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadHodingRegister,
                Start = start,
                Length = length
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步读取输入寄存器 (0x04 - Read Input Registers)
        /// </summary>
        public static Rx<byte[]> ReadInputRegisters(this IModBus modBus, byte slaveId, ushort start, ushort length)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 125.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadInputRegister,
                Start = start,
                Length = length
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步读取输入寄存器 (0x04 - Read Input Registers)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">读取数量（寄存器数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> ReadInputRegistersAsync(this IModBus modBus, byte slaveId, ushort start, ushort length, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 125)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 125.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.ReadInputRegister,
                Start = start,
                Length = length
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步写单线圈 (0x05 - Write Single Coil)
        /// </summary>
        public static Rx<byte[]> WriteSingleCoil(this IModBus modBus, byte slaveId, ushort start, bool value)
        {
            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteCoils,
                Start = start,
                Length = 1,
                Data = [(byte)(value ? 0xFF : 0x00), 0x00]
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步写单线圈 (0x05 - Write Single Coil)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="value">要写入的布尔值 (true: 0xFF00, false: 0x0000)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> WriteSingleCoilAsync(this IModBus modBus, byte slaveId, ushort start, bool value, CancellationToken cancellationToken = default)
        {
            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteCoils,
                Start = start,
                Length = 1,
                Data = [(byte)(value ? 0xFF : 0x00), 0x00]
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步写单保持寄存器 (0x06 - Write Single Holding Register)
        /// </summary>
        public static Rx<byte[]> WriteSingleRegister(this IModBus modBus, byte slaveId, ushort start, ushort value)
        {
            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteHodingRegister,
                Start = start,
                Length = 1,
                Data = BitExtentions.ToBytesByBigEndian(value)
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步写单保持寄存器 (0x06 - Write Single Holding Register)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="value">要写入的寄存器值</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> WriteSingleRegisterAsync(this IModBus modBus, byte slaveId, ushort start, ushort value, CancellationToken cancellationToken = default)
        {
            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteHodingRegister,
                Start = start,
                Length = 1,
                Data = BitExtentions.ToBytesByBigEndian(value)
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步写多线圈 (0x0F - Write Multiple Coils)
        /// </summary>
        public static Rx<byte[]> WriteMultipleCoils(this IModBus modBus, byte slaveId, ushort start, ushort length, byte[] data)
        {
            if (length == 0 || length > 1968)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 1968.");
            if (data == null || data.Length == 0)
                throw new ArgumentException("The data cannot be null or empty.", nameof(data));

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteMultiCoils,
                Start = start,
                Length = length,
                Data = data
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步写多线圈 (0x0F - Write Multiple Coils)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="length">写入数量（线圈数）</param>
        /// <param name="data">打包后的线圈数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> WriteMultipleCoilsAsync(this IModBus modBus, byte slaveId, ushort start, ushort length, byte[] data, CancellationToken cancellationToken = default)
        {
            if (length == 0 || length > 1968)
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and 1968.");
            if (data == null || data.Length == 0)
                throw new ArgumentException("The data cannot be null or empty.", nameof(data));

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteMultiCoils,
                Start = start,
                Length = length,
                Data = data
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }

        /// <summary>
        /// 同步写多保持寄存器 (0x10 - Write Multiple Holding Registers)
        /// </summary>
        public static Rx<byte[]> WriteMultipleRegisters(this IModBus modBus, byte slaveId, ushort start, ushort[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("The values cannot be null or empty.", nameof(values));
            if (values.Length > 123)
                throw new ArgumentOutOfRangeException(nameof(values), "The length must be between 1 and 123.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteMultiHodingRegister,
                Start = start,
                Length = (ushort)values.Length,
                Data = values.ToByteArrayBigEndian()
            };

            return modBus.Request(tx);
        }

        /// <summary>
        /// 异步写多保持寄存器 (0x10 - Write Multiple Holding Registers)
        /// </summary>
        /// <param name="modBus">IModBus 实例</param>
        /// <param name="slaveId">从站 ID</param>
        /// <param name="start">起始地址</param>
        /// <param name="values">要写入的寄存器值数组</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>响应结果</returns>
        public static async Task<Rx<byte[]>> WriteMultipleRegistersAsync(this IModBus modBus, byte slaveId, ushort start, ushort[] values, CancellationToken cancellationToken = default)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("The values cannot be null or empty.", nameof(values));
            if (values.Length > 123)
                throw new ArgumentOutOfRangeException(nameof(values), "The length must be between 1 and 123.");

            var tx = new Tx
            {
                ProtocolType = modBus.ProtocolType,
                SlaveId = slaveId,
                FunctionCode = ModBusFunctionCode.WriteMultiHodingRegister,
                Start = start,
                Length = (ushort)values.Length,
                Data = values.ToByteArrayBigEndian()
            };

            return await modBus.RequestAsync(tx, cancellationToken);
        }
    }
}
