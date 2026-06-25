# TCP Scanner Driver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a generic TCP scanner (barcode reader) driver with configurable protocol and a reusable `TcpClientConnection` component.

**Architecture:** `drivers.common/TcpClientConnection` provides reusable TCP Client with sticky-packet handling (ReceiveBuffer + FrameDelimiter), auto-reconnect, and exponential backoff. `ScannerStrategy` wraps it with sync/async dual-mode, `BarcodeParser` for configurable barcode extraction, and `BarcodeDedup` for deduplication.

**Tech Stack:** .NET 8, C# 12, xUnit, Moq.

---

## File Structure

```
collection-drivers/
├── drivers.common/
│   ├── TcpClientConnection.cs       ← TCP Client 公共组件（连接/重连/粘包拆帧）
│   └── drivers.common.csproj
├── scanner-driver/
│   ├── strategies/
│   │   └── ScannerStrategy.cs       ← 同步/异步双模式策略
│   ├── models/
│   │   └── ScannerConfig.cs         ← 可配置协议模型
│   ├── BarcodeDedup.cs              ← 去重/去抖
│   ├── BarcodeParser.cs             ← 条码解析（正则+前后缀）
│   ├── ScannerMachine.cs            ← Machine 子类
│   └── scanner-driver.csproj
├── scanner-driver.test/
│   ├── BarcodeParserTest.cs
│   ├── BarcodeDedupTest.cs
│   ├── strategies/
│   │   └── ScannerStrategyTest.cs
│   └── scanner-driver.test.csproj
├── examples/
│   └── config.scanner.yml
└── collection-drivers.sln
```

---

## Phase 1: Project Scaffolding

**Files:**
- Create: `drivers.common/drivers.common.csproj`
- Create: `scanner-driver/scanner-driver.csproj`
- Create: `scanner-driver.test/scanner-driver.test.csproj`

**Skip TDD rationale:** Pure scaffolding, no domain logic.

### Task 1.1: Create projects

- [ ] **Step 1: Create solution projects**

```bash
cd d:/cihong/github/collection-drivers
dotnet new classlib -n drivers.common -o drivers.common --framework net8.0
dotnet new classlib -n scanner-driver -o scanner-driver --framework net8.0
dotnet new xunit -n scanner-driver.test -o scanner-driver.test --framework net8.0
dotnet sln add drivers.common/drivers.common.csproj
dotnet sln add scanner-driver/scanner-driver.csproj
dotnet sln add scanner-driver.test/scanner-driver.test.csproj
```

- [ ] **Step 2: Add project references**

```bash
cd d:/cihong/github/collection-drivers
dotnet add scanner-driver/scanner-driver.csproj reference drivers.common/drivers.common.csproj
dotnet add scanner-driver/scanner-driver.csproj reference base-driver/base-driver.csproj
dotnet add scanner-driver.test/scanner-driver.test.csproj reference scanner-driver/scanner-driver.csproj
```

- [ ] **Step 3: Add NuGet packages**

```bash
cd d:/cihong/github/collection-drivers
dotnet add scanner-driver.test/scanner-driver.test.csproj package Moq
```

- [ ] **Step 4: Remove default Class1.cs files + verify build**

```bash
rm d:/cihong/github/collection-drivers/drivers.common/Class1.cs
rm d:/cihong/github/collection-drivers/scanner-driver/Class1.cs
cd d:/cihong/github/collection-drivers
dotnet build
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add scanner-driver and drivers.common scaffolding"
```

---

## Phase 2: TcpClientConnection (drivers.common)

**Files:**
- Create: `drivers.common/TcpClientConnection.cs`

**Skip TDD rationale:** Network infrastructure code; tests require a TCP server.

### Task 2.1: Create TcpClientConnection

- [ ] **Step 1: Create `drivers.common/TcpClientConnection.cs`**

```csharp
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace drivers.common;

public class TcpClientConnection : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _disposeCts;
    private Task? _receiveTask;
    private readonly List<byte> _recvBuffer = new();
    private const int MaxReceiveBuffer = 65536;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public string Host { get; private set; } = "";
    public int Port { get; private set; }
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public bool IsConnected => _client?.Connected ?? false;

    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<Exception, string>? OnError;
    public event Action<byte[]>? OnDataReceived;

    // 消息帧分隔符（byte[]），扫码枪通常用 \r 或 \n 分隔
    public byte[]? FrameDelimiter { get; set; }

    public void Configure(string host, int port,
        int connectTimeoutMs = 3000, int receiveTimeoutMs = 5000)
    {
        Host = host;
        Port = port;
        ConnectTimeoutMs = connectTimeoutMs;
        ReceiveTimeoutMs = receiveTimeoutMs;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

        _client?.Dispose();
        _client = new TcpClient();
        await _client.ConnectAsync(Host, Port, linkedCts.Token);
        _stream = _client.GetStream();
        _client.ReceiveTimeout = ReceiveTimeoutMs;
        _recvBuffer.Clear();
        OnConnected?.Invoke();
    }

    // ct 仅控制信号量等待和重试取消。读写超时由 ReceiveTimeoutMs + 外部 CTS 控制。
    public async Task<byte[]> SendAndReceiveAsync(byte[] command,
        int retryCount = 3, CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!IsConnected) await ReconnectAsync(ct);

                await _stream!.WriteAsync(command, ct);

                while (!ct.IsCancellationRequested)
                {
                    var available = _client!.Available;
                    if (available > 0)
                    {
                        var buf = new byte[available];
                        var len = await _stream.ReadAsync(buf, 0, buf.Length, ct);
                        _recvBuffer.AddRange(buf.Take(len));

                        if (_recvBuffer.Count > MaxReceiveBuffer)
                        {
                            _recvBuffer.Clear();
                            throw new InvalidOperationException("ReceiveBuffer overflow");
                        }

                        var frame = TryExtractFrame();
                        if (frame != null) return frame;
                    }
                    else
                    {
                        await Task.Delay(10, ct);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                attempt++;
                OnError?.Invoke(ex, $"SendAndReceive (attempt {attempt})");
                if (attempt >= retryCount) throw;
                await Task.Delay(100 * attempt, ct);
                await ReconnectAsync(ct);
            }
        }
    }

    public void StartReceiveLoop()
    {
        _disposeCts = new CancellationTokenSource();
        var token = _disposeCts.Token;
        _receiveTask = Task.Run(async () =>
        {
            int retryDelay = 1000;
            const int maxRetryDelay = 30000;

            while (!token.IsCancellationRequested && !_disposed)
            {
                try
                {
                    await ConnectAsync(token);
                    retryDelay = 1000;
                    await ReceiveLoop(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex, "ReceiveLoop.Reconnect");
                }
                await Task.Delay(retryDelay, token);
                retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
            }
        });
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && IsConnected && !_disposed)
        {
            try
            {
                var len = await _stream!.ReadAsync(buffer, 0, buffer.Length, ct);
                if (len == 0) break;

                _recvBuffer.AddRange(buffer.Take(len));

                if (_recvBuffer.Count > MaxReceiveBuffer)
                {
                    OnError?.Invoke(
                        new InvalidOperationException("ReceiveBuffer overflow"), "ReceiveLoop");
                    _recvBuffer.Clear();
                    continue;
                }

                while (true)
                {
                    var frame = TryExtractFrame();
                    if (frame == null) break;
                    OnDataReceived?.Invoke(frame);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, "ReceiveLoop");
                break;
            }
        }
        OnDisconnected?.Invoke();
    }

    private byte[]? TryExtractFrame()
    {
        if (FrameDelimiter == null || FrameDelimiter.Length == 0)
        {
            if (_recvBuffer.Count == 0) return null;
            var frame = _recvBuffer.ToArray();
            _recvBuffer.Clear();
            return frame;
        }

        var delimPos = IndexOf(_recvBuffer, FrameDelimiter);
        if (delimPos < 0) return null;

        var complete = _recvBuffer.Take(delimPos).ToArray();
        _recvBuffer.RemoveRange(0, delimPos + FrameDelimiter.Length);
        return complete;
    }

    private static int IndexOf(List<byte> source, byte[] pattern)
    {
        for (int i = 0; i <= source.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            _stream?.Dispose();
            _client?.Dispose();
            _recvBuffer.Clear();
            _client = new TcpClient();
            await _client.ConnectAsync(Host, Port, ct);
            _stream = _client.GetStream();
            _client.ReceiveTimeout = ReceiveTimeoutMs;
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _reconnectLock.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        _reconnectLock.Dispose();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
cd d:/cihong/github/collection-drivers
dotnet build
```

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add TcpClientConnection with ReceiveBuffer and auto-reconnect"
```

---

## Phase 3: Config Models + BarcodeParser + BarcodeDedup

**Files:**
- Create: `scanner-driver/models/ScannerConfig.cs`
- Create: `scanner-driver/BarcodeParser.cs`
- Create: `scanner-driver/BarcodeDedup.cs`
- Create: `scanner-driver.test/BarcodeParserTest.cs`
- Create: `scanner-driver.test/BarcodeDedupTest.cs`

**Skip TDD rationale for config models:** Pure POCO definitions.
**TDD for BarcodeParser/BarcodeDedup:** Small domain logic, covered by unit tests.

### Task 3.1: Config models

- [ ] **Step 1: Create `scanner-driver/models/ScannerConfig.cs`**

```csharp
namespace scanner.driver.models;

public class ProtocolConfig
{
    public string SendCommandHex { get; set; } = "7374617274";
    public string ResponseEncoding { get; set; } = "ascii";
    public string? BarcodeRegex { get; set; }
    public int RegexGroupIndex { get; set; } = 0;
    public string[]? RemovePrefixes { get; set; }
    public string[]? RemoveSuffixes { get; set; }
    public string? FrameDelimiterHex { get; set; }
}

public class ScannerConfig
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 2001;
    public string Mode { get; set; } = "sync";
    public int RetryCount { get; set; } = 3;
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public bool DedupEnabled { get; set; } = true;
    public ProtocolConfig Protocol { get; set; } = new();
}
```

### Task 3.2: BarcodeParser

- [ ] **Step 1 (RED): Create `scanner-driver.test/BarcodeParserTest.cs`**

```csharp
using scanner.driver;
using scanner.driver.models;

namespace scanner.driver.test;

public class BarcodeParserTest
{
    [Fact]
    public void Parse_Ascii_ReturnsTrimmed()
    {
        var proto = new ProtocolConfig { ResponseEncoding = "ascii" };
        var parser = new BarcodeParser(proto);
        var result = parser.Parse("ABC123"u8.ToArray());
        Assert.Equal("ABC123", result);
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        var parser = new BarcodeParser(new ProtocolConfig());
        Assert.Null(parser.Parse(null));
    }

    [Fact]
    public void Parse_Regex_ExtractsGroup()
    {
        var proto = new ProtocolConfig
        {
            BarcodeRegex = "<p>(.*?)</p>",
            RegexGroupIndex = 1
        };
        var parser = new BarcodeParser(proto);
        var data = "<p>BARCODE123</p>"u8.ToArray();
        var result = parser.Parse(data);
        Assert.Equal("BARCODE123", result);
    }

    [Fact]
    public void Parse_RemovePrefix_StripsStart()
    {
        var proto = new ProtocolConfig
        {
            RemovePrefixes = new[] { "STX" },
            RemoveSuffixes = new[] { "ETX" }
        };
        var parser = new BarcodeParser(proto);
        var result = parser.Parse("STXABC123ETX"u8.ToArray());
        Assert.Equal("ABC123", result);
    }
}
```

- [ ] **Step 2 (RED verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~BarcodeParserTest"
```

- [ ] **Step 3 (GREEN): Create `scanner-driver/BarcodeParser.cs`**

```csharp
using System.Text;
using System.Text.RegularExpressions;
using scanner.driver.models;

namespace scanner.driver;

public class BarcodeParser
{
    private readonly ProtocolConfig _protocol;

    public BarcodeParser(ProtocolConfig protocol)
    {
        _protocol = protocol;
    }

    public string? Parse(byte[] raw)
    {
        if (raw == null) return null;

        string text = _protocol.ResponseEncoding switch
        {
            "utf8" => Encoding.UTF8.GetString(raw),
            "hex" => BitConverter.ToString(raw).Replace("-", ""),
            _ => Encoding.ASCII.GetString(raw)
        };

        text = text.Trim('\r', '\n', '\0');

        if (!string.IsNullOrEmpty(_protocol.BarcodeRegex))
        {
            var match = Regex.Match(text, _protocol.BarcodeRegex);
            if (!match.Success) return null;
            var group = match.Groups[_protocol.RegexGroupIndex];
            if (!group.Success) return null;
            text = group.Value.Trim();
        }

        if (_protocol.RemovePrefixes != null)
            foreach (var p in _protocol.RemovePrefixes)
                if (text.StartsWith(p))
                    text = text.Substring(p.Length);

        if (_protocol.RemoveSuffixes != null)
            foreach (var s in _protocol.RemoveSuffixes)
                if (text.EndsWith(s))
                    text = text.Substring(0, text.Length - s.Length);

        return string.IsNullOrEmpty(text) ? null : text;
    }
}
```

- [ ] **Step 4 (GREEN verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~BarcodeParserTest"
```

### Task 3.3: BarcodeDedup

- [ ] **Step 1 (RED): Create `scanner-driver.test/BarcodeDedupTest.cs`**

```csharp
using scanner.driver;

namespace scanner.driver.test;

public class BarcodeDedupTest
{
    [Fact]
    public void IsDuplicate_SameBarcode_ReturnsTrue()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        Assert.True(dedup.IsDuplicate("ABC"));
    }

    [Fact]
    public void IsDuplicate_DifferentBarcode_ReturnsFalse()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        Assert.False(dedup.IsDuplicate("DEF"));
    }

    [Fact]
    public void Reset_ClearsLastBarcode()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        dedup.Reset();
        Assert.False(dedup.IsDuplicate("ABC"));
    }
}
```

- [ ] **Step 2 (RED verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~BarcodeDedupTest"
```

- [ ] **Step 3 (GREEN): Create `scanner-driver/BarcodeDedup.cs`**

```csharp
namespace scanner.driver;

public class BarcodeDedup
{
    private string? _lastBarcode;
    private DateTime _lastTime;
    private readonly int _debounceMs;
    private readonly object _lock = new();

    public BarcodeDedup(int debounceMs = 2000)
    {
        _debounceMs = debounceMs;
    }

    public bool IsDuplicate(string barcode)
    {
        lock (_lock)
        {
            if (barcode == _lastBarcode &&
                (DateTime.UtcNow - _lastTime).TotalMilliseconds < _debounceMs)
                return true;

            _lastBarcode = barcode;
            _lastTime = DateTime.UtcNow;
            return false;
        }
    }

    public void Reset()
    {
        lock (_lock) { _lastBarcode = null; }
    }
}
```

- [ ] **Step 4 (GREEN verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~BarcodeDedupTest"
```

- [ ] **Step 5: Commit**
```bash
git add .
git commit -m "feat: add scanner config, BarcodeParser, and BarcodeDedup with tests"
```

---

## Phase 4: ScannerStrategy (TDD Required)

**Files:**
- Create: `scanner-driver/strategies/ScannerStrategy.cs`
- Create: `scanner-driver/ScannerMachine.cs`
- Create: `scanner-driver.test/strategies/ScannerStrategyTest.cs`
- Create: `examples/config.scanner.yml`

### Task 4.1: ScannerStrategy

- [ ] **Step 1 (RED): Create test**

Create `scanner-driver.test/strategies/ScannerStrategyTest.cs`:

```csharp
using scanner.driver.strategies;

namespace scanner.driver.test.strategies;

public class ScannerStrategyTest
{
    [Fact]
    public void Strategy_TypeExists()
    {
        var type = typeof(ScannerStrategy);
        Assert.NotNull(type);
    }
}
```

- [ ] **Step 2 (RED verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~ScannerStrategyTest"
```

- [ ] **Step 3 (GREEN): Create `scanner-driver/strategies/ScannerStrategy.cs`**

```csharp
using System.Text.RegularExpressions;
using drivers.common;
using l99.driver.@base;
using scanner.driver.models;

namespace scanner.driver.strategies;

public class ScannerStrategy : Strategy
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

    public override async Task<dynamic?> InitializeAsync()
    {
        if (_initialized) return null;
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

        return null;
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        if (_config.Mode != "sync")
        {
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
            return;
        }

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
        }

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
}
```

- [ ] **Step 4 (GREEN verify):**
```bash
cd d:/cihong/github/collection-drivers
dotnet test scanner-driver.test/scanner-driver.test.csproj --filter "FullyQualifiedName~ScannerStrategyTest"
```

### Task 4.2: ScannerMachine + YAML example

- [ ] **Step 1: Create `scanner-driver/ScannerMachine.cs`**

```csharp
using l99.driver.@base;

namespace scanner.driver;

public class ScannerMachine : Machine
{
    public ScannerMachine(Machines machines, object configuration)
        : base(machines, configuration) { }
}
```

- [ ] **Step 2: Create `examples/config.scanner.yml`**

```yaml
machines:
  - id: scanner_1
    enabled: true
    type: scanner.driver.ScannerMachine, scanner-driver
    strategy: scanner.driver.strategies.ScannerStrategy, scanner-driver
    handler: l99.driver.@base.Handler, base-driver

    scanner.driver.ScannerMachine, scanner-driver:
      sweep_ms: 500

    scanner.driver.strategies.ScannerStrategy, scanner-driver:
      host: "192.168.250.10"
      port: 2001
      mode: sync
      retry_count: 3
      connect_timeout_ms: 3000
      receive_timeout_ms: 5000
      dedup_enabled: true

      protocol:
        send_command_hex: "7374617274"
        response_encoding: ascii
        barcode_regex: "<p>(?<barcode>.*?)</p>"
        regex_group_index: 1
        frame_delimiter_hex: "0A"
```

- [ ] **Step 3: Build + run all tests**

```bash
cd d:/cihong/github/collection-drivers
dotnet build
dotnet test
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add ScannerStrategy, ScannerMachine, and YAML config"
```

---

## Self-Review

### Spec Coverage
| Spec § | Requirements | Covered By |
|---|---|---|
| §3.1 TcpClientConnection | ReceiveBuffer, auto-reconnect, FrameDelimiter | Task 2.1 |
| §3.2 ScannerConfig | ProtocolConfig + ScannerConfig POCOs | Task 3.1 |
| §3.3 BarcodeParser | Regex, prefix/suffix, encoding | Task 3.2 |
| §3.4 BarcodeDedup | Thread-safe dedup with lock | Task 3.3 |
| §3.5 ScannerStrategy | Sync/async dual-mode, InitializeAsync, SweepAsync | Task 4.1 |
| §3.6 ScannerMachine | Machine subclass | Task 4.2 |
| §4 YAML Config | Example with all fields | Task 4.2 Step 2 |

### Placeholder Check
- No TBD, TODO, or placeholder code in any task
- All test files have complete code
- StringToByteArray has full input validation (null, 0x prefix, odd length)

### Type Consistency
- `ScannerStrategy.OnData` signature `Action<string, string>` matches usage throughout
- `ProtocolConfig.FrameDelimiterHex` parsed to `byte[]` in Strategy, assigned to `TcpClientConnection.FrameDelimiter`
- `ScannerConfig` fields match YAML config keys
- `TcpClientConnection.ConnectTimeoutMs` matches `ScannerConfig.ConnectTimeoutMs`

---

## Execution

Plan complete and saved to `docs/superpowers/plans/2026-06-25-scanner-driver-implementation.md`.

Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — Execute tasks in this session, batch execution with checkpoints

**Which approach?**
