using Communication.Modbus.Core;
using Communication.Modbus.RTU;
using Communication.Modbus.TCP;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace Communication.Modbus.Factory
{
    public sealed class ModbusFactory : IModbusFactory
    {
        private readonly ConcurrentDictionary<string, IModbus> modbusList = new();
        private readonly ILogger<ModbusFactory> logger;
        private readonly ILoggerFactory loggerFactory;

        private bool disposed;

        public int Count => modbusList.Count;
        public IEnumerable<string> Keys => modbusList.Keys;

        public ModbusFactory() 
               : this(NullLogger<ModbusFactory>.Instance, NullLoggerFactory.Instance)
        {
        }

        public ModbusFactory(ILogger<ModbusFactory> logger, ILoggerFactory loggerFactory)
        {
            this.logger = logger ?? NullLogger<ModbusFactory>.Instance;
            this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            ResponseParser.SetLogger(this.loggerFactory);
        }


        public IModbus? Get(string key)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
                return null;
            modbusList.TryGetValue(key, out var modbus);
            return modbus;
        }

        public TResult GetRequired<TResult>(string key) where TResult : class, IModbus
        {
            var modbus = Get(key);
            if (modbus is TResult typed)
                return typed;
            throw new ModbusException(ModbusErrorCode.GatewayUnavailable,
                $"Modbus instance '{key}' not found or type mismatch. Expected {typeof(TResult).Name}.");
        }


        public IModbus GetOrAdd(string key, ModbusTCPConfig config)
        {
            ThrowIfDisposed();
            ValidateAndFillDefaults(config, key);
            logger.LogInformation("GetOrAdd TCP: key={Key}, address={Address}:{Port}", key, config.Address, config.Port);

            return modbusList.GetOrAdd(key, _ =>
            {
                var tcp = new ModbusTCP(config, loggerFactory.CreateLogger<ModbusTCP>());
                logger.LogDebug("Created ModbusTCP: key={Key}", key);
                return tcp;
            });
        }

        // ── GetOrAdd RTU ────────────────────────

        public IModbus GetOrAdd(string key, ModbusRTUConfig config)
        {
            ThrowIfDisposed();
            ValidateAndFillDefaults(config, key);
            logger.LogInformation("GetOrAdd RTU: key={Key}, port={PortName}", key, config.PortName);

            return modbusList.GetOrAdd(key, _ =>
            {
                var rtu = new ModbusRTU(config, loggerFactory.CreateLogger<ModbusRTU>());
                logger.LogDebug("Created ModbusRTU: key={Key}", key);
                return rtu;
            });
        }

        // ── TryGet ──────────────────────────────

        public bool TryGet(string key, out IModbus? modbus)
        {
            ThrowIfDisposed();
            modbus = null;
            if (string.IsNullOrEmpty(key))
                return false;
            return modbusList.TryGetValue(key, out modbus);
        }

        // ── TryAdd TCP ──────────────────────────

        public bool TryAdd(string key, ModbusTCPConfig config, out IModbus? modbus)
        {
            ThrowIfDisposed();
            modbus = null;
            ValidateAndFillDefaults(config, key);
            logger.LogInformation("TryAdd TCP: key={Key}, address={Address}:{Port}", key, config.Address, config.Port);

            modbus = new ModbusTCP(config);
            if (!modbusList.TryAdd(key, modbus))
            {
                modbus.Dispose();
                modbus = null;
                logger.LogWarning("TryAdd TCP failed: key {Key} already exists", key);
                return false;
            }

            logger.LogDebug("Added ModbusTCP: key={Key}", key);
            return true;
        }

        // ── TryAdd RTU ──────────────────────────

        public bool TryAdd(string key, ModbusRTUConfig config, out IModbus? modbus)
        {
            ThrowIfDisposed();
            modbus = null;
            ValidateAndFillDefaults(config, key);
            logger.LogInformation("TryAdd RTU: key={Key}, port={PortName}", key, config.PortName);

            modbus = new ModbusRTU(config);
            if (!modbusList.TryAdd(key, modbus))
            {
                modbus.Dispose();
                modbus = null;
                logger.LogWarning("TryAdd RTU failed: key {Key} already exists", key);
                return false;
            }

            logger.LogDebug("Added ModbusRTU: key={Key}", key);
            return true;
        }

        // ── TryRemove ───────────────────────────

        public bool TryRemove(string key)
        {
            ThrowIfDisposed();
            if (!modbusList.TryRemove(key, out var modbus))
                return false;
            modbus.Dispose();
            logger.LogInformation("Removed Modbus instance: key={Key}", key);
            return true;
        }

        // ── IDisposable / IAsyncDisposable ──────

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var kvp in modbusList)
            {
                try
                {
                    kvp.Value.Dispose();
                    logger.LogDebug("Disposed Modbus instance: key={Key}", kvp.Key);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing Modbus instance: key={Key}", kvp.Key);
                }
            }
            modbusList.Clear();
            logger.LogInformation("ModbusFactory disposed ({Count} instances)", modbusList.Count);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            disposed = true;

            var tasks = modbusList.Select(kvp =>
            {
                try
                {
                    if (kvp.Value is IAsyncDisposable ad)
                        return ad.DisposeAsync().AsTask();
                    kvp.Value.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing Modbus instance: key={Key}", kvp.Key);
                }
                return Task.CompletedTask;
            }).ToList();

            await Task.WhenAll(tasks);
            modbusList.Clear();
            logger.LogInformation("ModbusFactory disposed async ({Count} instances)", modbusList.Count);
        }

        // ── Validation ──────────────────────────

        private static void ValidateAndFillDefaults(ModbusTCPConfig config, string key)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key must not be null or empty.", nameof(key));
            if (string.IsNullOrEmpty(config.Address))
                config.Address = "127.0.0.1";
            if (config.Port == 0)
                config.SetPort(502);
            if (config.ReadTimeOut <= 0)
                config.ReadTimeOut = 2000;
            if (config.WriteTimeOut <= 0)
                config.WriteTimeOut = 2000;
            if (config.ConnectTimeout <= 0)
                config.ConnectTimeout = 2000;
        }

        private static void ValidateAndFillDefaults(ModbusRTUConfig config, string key)
        {
            ArgumentNullException.ThrowIfNull(config);
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key must not be null or empty.", nameof(key));
            if (string.IsNullOrEmpty(config.PortName))
                throw new ModbusException(ModbusErrorCode.GatewayUnavailable,
                    "PortName must not be null or empty.");
            if (config.BaudRate <= 0)
                config.BaudRate = 9600;
            if (config.DataBits < 5 || config.DataBits > 8)
                config.DataBits = 8;
            if (config.ReadTimeOut <= 0)
                config.ReadTimeOut = 2000;
            if (config.WriteTimeOut <= 0)
                config.WriteTimeOut = 2000;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ModbusFactory));
        }
    }
}
