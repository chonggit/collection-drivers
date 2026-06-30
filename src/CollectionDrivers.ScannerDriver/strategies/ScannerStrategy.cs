using CollectionDrivers.Common;
using CollectionDrivers.ScannerDriver.Models;

namespace CollectionDrivers.ScannerDriver.Strategies;

public class ScannerStrategy : Strategy, IDisposable
{
    private readonly TcpClientConnection _connection = new();
    private readonly ScannerConfig _config;
    private readonly BarcodeParser _parser;
    private readonly BarcodeDedup _dedup = new();
    private readonly byte[] _command;
    private volatile bool _initialized;

    public event Action<string, string>? OnData;
    public event Action<Exception, string>? OnError;

    public ScannerStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = ParseConfig(rawConfig);
        _config.Name = machine.Id;
        _parser = new BarcodeParser(_config.Protocol);
        _command = StringToByteArray(_config.Protocol.SendCommandHex);

        _connection.Configure(_config.Host, _config.Port,
            _config.ConnectTimeoutMs, _config.ReceiveTimeoutMs);

        if (!string.IsNullOrEmpty(_config.Protocol.FrameDelimiterHex))
            _connection.FrameDelimiter = StringToByteArray(
                _config.Protocol.FrameDelimiterHex);
    }

    public override Task InitializeAsync()
    {
        if (_initialized) return Task.CompletedTask;
        _initialized = true;

        if (_config.Mode != "sync" && _config.Mode != "async")
            throw new ArgumentException(
                $"Invalid scanner mode '{_config.Mode}'. Must be 'sync' or 'async'.");

        _connection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);

        if (_config.Mode == "async")
        {
            _connection.OnDataReceived += OnRawData;
            _connection.StartReceiveLoop();
        }

        return Task.CompletedTask;
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        if (_config.Mode != "sync")
        {
            // async 模式：sweep 仅作心跳，数据通过事件驱动 OnData 上报
            LastSuccess = true;
            IsHealthy = true;
            if (Machine?.Handler != null)
                await Machine.Handler.OnStrategySweepCompleteInternalAsync();
            return;
        }

        bool allSuccess = true;

        try
        {
            using var sweepCts = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(_config.ReceiveTimeoutMs));
            var raw = await _connection.SendAndReceiveAsync(
                _command, _config.RetryCount, sweepCts.Token);
            var barcode = _parser.Parse(raw);
            if (barcode != null)
            {
                if (!_config.DedupEnabled || !_dedup.IsDuplicate(barcode))
                    OnData?.Invoke(_config.Name, barcode);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, $"Scanner={_config.Name}");
            allSuccess = false;
        }

        LastSuccess = allSuccess;
        IsHealthy = allSuccess;
        if (Machine?.Handler != null)
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }

    private void OnRawData(byte[] data)
    {
        var barcode = _parser.Parse(data);
        if (barcode != null)
        {
            if (!_config.DedupEnabled || !_dedup.IsDuplicate(barcode))
                OnData?.Invoke(_config.Name, barcode);
        }
    }

    private static byte[] StringToByteArray(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string must not be null or empty");

        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);

        if (string.IsNullOrEmpty(hex))
            throw new ArgumentException("Hex string contains only '0x' prefix, no actual hex digits");

        if (hex.Length % 2 != 0 || !hex.All(c => "0123456789abcdefABCDEF".Contains(c)))
            throw new ArgumentException(
                $"Invalid hex string: '{hex}' (must be even-length hex)");

        int len = hex.Length / 2;
        var bytes = new byte[len];
        for (int i = 0; i < len; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }

    private static ScannerConfig ParseConfig(dynamic rawConfig)
    {
        var config = new ScannerConfig();
        if (rawConfig == null) return config;

        IDictionary<string, object>? dict = rawConfig as IDictionary<string, object>;
        if (dict == null)
        {
            var objDict = rawConfig as IDictionary<object, object>;
            if (objDict == null) return config;
            dict = objDict.ToDictionary(k => k.Key.ToString()!, k => k.Value!);
        }

        if (dict.ContainsKey("host")) config.Host = (string)dict["host"];
        if (dict.ContainsKey("port")) config.Port = Convert.ToInt32(dict["port"]);
        if (dict.ContainsKey("mode")) config.Mode = (string)dict["mode"];
        if (dict.ContainsKey("retry_count")) config.RetryCount = Convert.ToInt32(dict["retry_count"]);
        if (dict.ContainsKey("connect_timeout_ms")) config.ConnectTimeoutMs = Convert.ToInt32(dict["connect_timeout_ms"]);
        if (dict.ContainsKey("receive_timeout_ms")) config.ReceiveTimeoutMs = Convert.ToInt32(dict["receive_timeout_ms"]);
        if (dict.ContainsKey("dedup_enabled")) config.DedupEnabled = (bool)dict["dedup_enabled"];

        if (dict.ContainsKey("protocol") && dict["protocol"] is IDictionary<string, object> proto)
        {
            if (proto.ContainsKey("send_command_hex")) config.Protocol.SendCommandHex = (string)proto["send_command_hex"];
            if (proto.ContainsKey("response_encoding")) config.Protocol.ResponseEncoding = (string)proto["response_encoding"];
            if (proto.ContainsKey("barcode_regex")) config.Protocol.BarcodeRegex = (string?)proto["barcode_regex"];
            if (proto.ContainsKey("regex_group_index")) config.Protocol.RegexGroupIndex = Convert.ToInt32(proto["regex_group_index"]);
            if (proto.ContainsKey("frame_delimiter_hex")) config.Protocol.FrameDelimiterHex = (string?)proto["frame_delimiter_hex"];
            if (proto.ContainsKey("remove_prefixes"))
                config.Protocol.RemovePrefixes = ((System.Collections.IList)proto["remove_prefixes"]).Cast<object>().Select(x => x.ToString()!).ToArray();
            if (proto.ContainsKey("remove_suffixes"))
                config.Protocol.RemoveSuffixes = ((System.Collections.IList)proto["remove_suffixes"]).Cast<object>().Select(x => x.ToString()!).ToArray();
        }

        return config;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
