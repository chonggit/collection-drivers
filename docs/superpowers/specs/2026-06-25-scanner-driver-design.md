# TCP Scanner Driver Design

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Build a **generic** TCP scanner (barcode reader) driver for the collection-drivers platform, following the base-driver architecture. The driver supports both sync (send command → wait response) and async (long-lived connection, push mode) scanning, with configurable protocol, dedup, and retry logic.

**Target project location:** `collection-drivers/scanner-driver/`

**Common TCP component:** `drivers.common/TcpClientConnection.cs` — reusable TCP Client connection manager extracted for all future TCP Client-based drivers.

**Reference implementation:** `HT_WCS/utility/myScannerMgr.cs` (myScanner + myScanner2 classes) from the wancang.turnmgr project.

---

## 2. Project Structure

```
collection-drivers/
├── base-driver/
├── battery-driver/
├── opcua-driver/
├── fins-driver/
├── drivers.common/                  ← 新：公共 TCP Client 组件
│   ├── TcpClientConnection.cs      ← TCP 连接/重连/发送/接收
│   └── drivers.common.csproj
├── scanner-driver/                  ← 新：扫码枪驱动
│   ├── strategies/
│   │   └── ScannerStrategy.cs      ← 同步/异步双模式
│   ├── models/
│   │   └── ScannerConfig.cs        ← 可配置协议模型
│   ├── BarcodeDedup.cs             ← 去重/去抖
│   ├── BarcodeParser.cs            ← 条码解析（正则+预处理）
│   ├── ScannerMachine.cs           ← Machine 子类
│   └── scanner-driver.csproj
├── scanner-driver.test/
├── examples/
│   └── config.scanner.yml
└── collection-drivers.sln
```

### 2.1 Key Design Decisions

- **drivers.common 抽取**：`TcpClientConnection` 封装 TCP Client 共用的连接/重连/发送/接收逻辑，供 scanner-driver 和未来 TCP Client 驱动复用。
- **可配置协议**：发送命令（hex）、响应编码、条码提取正则、前后缀移除均由 YAML 配置，不绑定特定品牌。
- **双模式**：sync 模式（SweepAsync 中发命令等响应）和 async 模式（长连接持续接收推送）。YAML 中 `mode` 字段控制。
- **去重/去抖**：`BarcodeDedup` 基于上次条码+时间窗口的轻量内存去重，不依赖文件系统。

---

## 3. Component Design

### 3.1 TcpClientConnection (drivers.common) — 含 ReceiveBuffer 粘包处理

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
    // 通过 ProtocolConfig.FrameDelimiterHex 配置
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

    /// <summary>发送命令并等待响应（sync 模式，带粘包处理）</summary>
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

                // 等待完整帧（读直到遇到分隔符或缓冲区超时）
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

                        // 尝试按分隔符拆帧
                        var frame = TryExtractFrame();
                        if (frame != null) return frame;
                    }
                    else
                    {
                        await Task.Delay(10, ct);
                    }
                }
            }
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

    /// <summary>启动持续接收（async 模式，断线自动重连）</summary>
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
                    retryDelay = 1000; // 连接成功恢复初始间隔
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
                        new InvalidOperationException("ReceiveBuffer overflow"),
                        "ReceiveLoop");
                    _recvBuffer.Clear();
                    continue;
                }

                // 尝试按分隔符拆帧
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

    /// <summary>从接收缓冲区提取完整帧</summary>
    private byte[]? TryExtractFrame()
    {
        if (FrameDelimiter == null || FrameDelimiter.Length == 0)
        {
            // 无分隔符：每次 Read 视为一帧
            if (_recvBuffer.Count == 0) return null;
            var frame = _recvBuffer.ToArray();
            _recvBuffer.Clear();
            return frame;
        }

        // 按分隔符拆帧
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
        // 等待重连锁释放后再释放（防止 SemaphoreFullException）
        _reconnectLock.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        _reconnectLock.Dispose();
    }
}
```

### 3.2 ScannerConfig

```csharp
// models/ProtocolConfig.cs
public class ProtocolConfig
{
    public string SendCommandHex { get; set; } = "7374617274"; // "start"
    public string ResponseEncoding { get; set; } = "ascii";
    public string? BarcodeRegex { get; set; }
    public int RegexGroupIndex { get; set; } = 0;
    public string[]? RemovePrefixes { get; set; }
    public string[]? RemoveSuffixes { get; set; }
    // TCP 粘包拆帧分隔符（hex），如 "0D" = \r, "0A" = \n, "0D0A" = \r\n
    public string? FrameDelimiterHex { get; set; }
}

// models/ScannerConfig.cs
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

### 3.3 BarcodeParser

```csharp
using System.Text;
using System.Text.RegularExpressions;

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

        // 编码转换
        string text = _protocol.ResponseEncoding switch
        {
            "utf8" => Encoding.UTF8.GetString(raw),
            "hex" => BitConverter.ToString(raw).Replace("-", ""),
            _ => Encoding.ASCII.GetString(raw)
        };

        text = text.Trim('\r', '\n', '\0');

        // 正则提取（优先于前后缀处理）
        if (!string.IsNullOrEmpty(_protocol.BarcodeRegex))
        {
            var match = Regex.Match(text, _protocol.BarcodeRegex);
            if (!match.Success) return null;
            var group = match.Groups[_protocol.RegexGroupIndex];
            if (!group.Success) return null;
            text = group.Value.Trim();
        }

        // 精确移除前缀（仅开头匹配）
        if (_protocol.RemovePrefixes != null)
            foreach (var p in _protocol.RemovePrefixes)
                if (text.StartsWith(p))
                    text = text.Substring(p.Length);

        // 精确移除后缀（仅结尾匹配）
        if (_protocol.RemoveSuffixes != null)
            foreach (var s in _protocol.RemoveSuffixes)
                if (text.EndsWith(s))
                    text = text.Substring(0, text.Length - s.Length);

        return string.IsNullOrEmpty(text) ? null : text;
    }
}
```

### 3.4 BarcodeDedup

```csharp
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

### 3.5 ScannerStrategy

```csharp
using drivers.common;

public class ScannerStrategy : Strategy
{
    private readonly TcpClientConnection _connection = new();
    private readonly ScannerConfig _config;
    private readonly BarcodeParser _parser;
    private readonly BarcodeDedup _dedup = new();
    private byte[] _command;

    public event Action<string, string>? OnData; // (scannerName, barcode)
    public event Action<Exception, string>? OnError;

    public ScannerStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = ParseConfig(rawConfig);
        _config.Name = machine.Id; // 从机器配置获取 scanner name
        _parser = new BarcodeParser(_config.Protocol);
        _command = StringToByteArray(_config.Protocol.SendCommandHex);

        _connection.Configure(_config.Host, _config.Port,
            _config.ConnectTimeoutMs, _config.ReceiveTimeoutMs);

        // 设置帧分隔符（用于 TCP 粘包拆帧）
        if (!string.IsNullOrEmpty(_config.Protocol.FrameDelimiterHex))
            _connection.FrameDelimiter = StringToByteArray(
                _config.Protocol.FrameDelimiterHex);
    }

    private volatile bool _initialized;

    public override async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        // 校验配置
        if (_config.Mode != "sync" && _config.Mode != "async")
            throw new ArgumentException(
                $"Invalid scanner mode '{_config.Mode}'. Must be 'sync' or 'async'.");

        // 在 InitializeAsync 中订阅事件（此时对象已完全构造）
        _connection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);

        if (_config.Mode == "async")
        {
            _connection.OnDataReceived += OnRawData;
            _connection.StartReceiveLoop();
        }
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

        // 自动剥离 0x/0X 前缀
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

    private static ScannerConfig ParseConfig(dynamic rawConfig) { /* ... */ }
}
```

### 3.6 ScannerMachine

```csharp
public class ScannerMachine : Machine
{
    public ScannerMachine(Machines machines, object configuration)
        : base(machines, configuration) { }
}
```

### 3.7 启动入口

推荐通过 YAML + `Machines.CreateMachines()` 启动。`ScannerStrategy` 通过 `OnData` 事件直接输出条码数据，宿主自行订阅消费。

编程式入口参考：
```csharp
// Program.cs
var machines = await Machines.CreateMachines(config);
machines.RunAsync(stoppingToken);

// 订阅条码事件（通过 ScannerStrategy 的 OnData）
```

---

## 4. YAML Configuration

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
        send_command_hex: "7374617274"       # "start"
        response_encoding: ascii
        # 使用正则提取条码（remove_prefixes/suffixes 二选一）
        barcode_regex: "<p>(?<barcode>.*?)</p>"
        regex_group_index: 1
        # TCP 粘包拆帧分隔符（扫码枪通常以换行分隔）
        frame_delimiter_hex: "0A"                # \n
        # 或不使用正则，用精确前后缀移除：
        # remove_prefixes: ["<p>"]
        # remove_suffixes: ["</p>"]
```

---

## 5. Data Flow

### Sync Mode
```
SweepAsync tick
  → SweepAsync → SendAndReceiveAsync(command)
    → ReceiveBuffer 粘包处理 → TryExtractFrame
    → BarcodeParser.Parse(raw) → 正则提取
    → BarcodeDedup.IsDuplicate()
    → OnData(scannerName, barcode)
    → OnStrategySweepCompleteInternalAsync()
```

### Async Mode
```
InitializeAsync → StartReceiveLoop
  → ConnectAsync → ReceiveLoop (断线自动重连)
    → ReceiveBuffer 粘包处理 → TryExtractFrame
    → OnRawData(data) → BarcodeParser.Parse
    → BarcodeDedup.IsDuplicate()
    → OnData(scannerName, barcode)
```

---

## 6. Error Handling

| Scenario | Behavior |
|---|---|
| 连接失败 | ConnectAsync 超时 → OnError + 自动重试（retry_count） |
| 扫码超时 | ReceiveTimeoutMs → OnError + ReconnectAsync |
| 接收缓冲区溢出 | MaxReceiveBuffer 超限 → 清空缓冲区 + OnError |
| 响应解析失败 | 正则不匹配 → 静默忽略 + OnError |
| 重复条码 | Dedup 过滤 → 不触发 OnData |
| 网络断线（sync） | SendAndReceiveAsync 自动 ReconnectAsync |
| 网络断线（async） | ReceiveLoop 退出 → StartReceiveLoop 自动重连 |
| TCP 粘包/半包 | ReceiveBuffer 按 FrameDelimiter 拆帧 |

---

## 7. Testing Strategy

| Component | Focus | Approach |
|---|---|---|
| `TcpClientConnection` | Connect/Send/Receive/Reconnect | Integration with TCP test server |
| `BarcodeParser` | 正则提取/编码转换/预处理 | Unit test with known byte arrays |
| `BarcodeDedup` | 去重/去抖逻辑 | Unit test with timestamps |
| `ScannerStrategy` | Sync/async dispatch | Mocked TcpClientConnection |
