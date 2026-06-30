using System.Linq;
using CollectionDrivers.Common;
using CollectionDrivers.FinsDriver.Models;

namespace CollectionDrivers.FinsDriver.Strategies;

public class FinsStrategy : Strategy, IDisposable
{
    private FinsConnection? _connection;
    private readonly FinsConfig _config;
    private bool _reconnecting;

    public event Action<string, ushort[]>? OnData;

    public FinsStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = ParseConfig(rawConfig);
    }

    private static FinsConfig ParseConfig(dynamic rawConfig)
    {
        var config = new FinsConfig();
        if (rawConfig == null) return config;

        IDictionary<string, object>? dict = rawConfig as IDictionary<string, object>;
        if (dict == null)
        {
            var objDict = rawConfig as IDictionary<object, object>;
            if (objDict == null) return config;
            dict = objDict.ToDictionary(k => k.Key.ToString()!, k => k.Value!);
        }

        if (dict.ContainsKey("remote_ip"))
            config.RemoteIp = (string)dict["remote_ip"];
        if (dict.ContainsKey("port"))
            config.Port = Convert.ToInt32(dict["port"]);
        if (dict.ContainsKey("timeout_ms"))
            config.TimeoutMs = Convert.ToInt32(dict["timeout_ms"]);

        if (dict.ContainsKey("collectors") && dict["collectors"] is System.Collections.IList cols)
        {
            var list = new List<FinsCollectorConfig>();
            foreach (var cObj in cols)
            {
                var c = cObj as IDictionary<string, object>;
                if (c == null) continue;
                list.Add(new FinsCollectorConfig
                {
                    Name = (string)c["name"],
                    StartAddress = Convert.ToUInt16(c["start_address"]),
                    Length = Convert.ToUInt16(c["length"])
                });
            }
            config.Collectors = list.ToArray();
        }

        return config;
    }

    public override async Task InitializeAsync()
    {
        _connection = new FinsConnection(
            _config.RemoteIp, _config.Port, _config.TimeoutMs);
        _connection.OnError += (ex, ctx) => RaiseOnError(ex, ctx);

        try
        {
            _connection.Connect();
            IsHealthy = true;
        }
        catch (Exception ex)
        {
            RaiseOnError(ex, "InitializeAsync.Connect");
            IsHealthy = false;
        }

        return;
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        if (_connection == null || !_connection.IsConnected)
        {
            TryReconnect();
            if (!_connection?.IsConnected ?? true)
            {
                LastSuccess = false;
                if (Machine?.Handler != null)
                    await Machine.Handler.OnStrategySweepCompleteInternalAsync();
                return;
            }
        }

        bool allSuccess = true;

        foreach (var collector in _config.Collectors)
        {
            try
            {
                var data = await _connection!.ReadDAsync(
                    collector.StartAddress, collector.Length);
                OnData?.Invoke(collector.Name, data);
            }
            catch (Exception ex)
            {
                RaiseOnError(ex, $"Sweep collector={collector.Name}");
                allSuccess = false;
            }
        }

        LastSuccess = allSuccess;
        IsHealthy = allSuccess;
        if (Machine?.Handler != null)
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }

    private void TryReconnect()
    {
        if (_reconnecting) return;
        _reconnecting = true;

        try
        {
            _connection?.Dispose();
            _connection = new FinsConnection(
                _config.RemoteIp, _config.Port, _config.TimeoutMs);
            _connection.OnError += (ex, ctx) => RaiseOnError(ex, ctx);
            _connection.Connect();
            IsHealthy = true;
        }
        catch (Exception ex)
        {
            RaiseOnError(ex, "TryReconnect");
            IsHealthy = false;
        }
        finally
        {
            _reconnecting = false;
        }
    }

    public async Task WriteDAsync(ushort address, ushort[] data, CancellationToken ct = default)
    {
        if (_connection == null)
            throw new InvalidOperationException("Not initialized");
        await _connection.WriteDAsync(address, data, ct);
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
