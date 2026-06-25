# FINS UDP Driver Design

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Build a **generic** Omron FINS UDP driver for the collection-drivers platform, following the base-driver architecture (`Machine → Strategy → Collector → Handler`). The driver reads/writes PLC memory areas (D registers, CIO, W, H) via the FINS UDP protocol, supporting periodic polling with a dual-thread command queue engine.

**Target project location:** `collection-drivers/fins-driver/`

**Key design principle:** Generic — not tied to any specific PLC configuration. Register addresses and polling schedules are entirely YAML-driven.

**Reference implementation:** `HT_WCS/utility/myFinsMgr.cs` from the wancang.turnmgr project.

---

## 2. Project Structure

```
collection-drivers/
├── base-driver/                  ← 已有框架
├── battery-driver/               ← 已有
├── opcua-driver/                 ← 已有
├── fins-driver/                  ← 新
│   ├── FinsConnection.cs         ← 封装 OmronFinsUDP.Net 客户端
│   ├── strategies/
│   │   └── FinsStrategy.cs      ← 轮询采集策略
│   ├── FinsMachine.cs            ← 继承 Machine
│   ├── models/
│   │   └── FinsConfig.cs         ← 配置模型
│   └── fins-driver.csproj
├── fins-driver.test/
├── examples/
│   └── config.fins.yml
└── collection-drivers.sln
```

### 2.1 Key Design Decisions

- **使用 NuGet 包**：通过 `lib/OmronFinsUDP.Net.dll`（`CableRobot.Fins.FinsClient`）实现 FINS UDP 通信，不自行实现协议帧。
- **FinsClient 封装**：`FinsConnection` 封装 `FinsClient`，提供配置注入和统一异常处理。
- **无 Collector 抽象层**：返回 `ushort[]` 数据，Strategy 通过事件 `OnData(collectorName, ushort[])` 对外暴露。宿主按需解析。YAGNI。
- **轮询模式**：SweepAsync 中调用 `FinsClient.ReadDataAsync` 批量读取寄存器。

---

## 3. Component Design

### 3.1 NuGet 引用

本地 DLL 路径：`lib/OmronFinsUDP.Net.dll`（`CableRobot.Fins.FinsClient`）

```xml
<Reference Include="OmronFinsUDP.Net">
  <HintPath>lib\OmronFinsUDP.Net.dll</HintPath>
</Reference>
```

`FinsClient` 提供完整 API：`ReadDataAsync`/`WriteDataAsync`，内部处理 UDP Socket、FINS 帧组包、响应解析。

### 3.2 FinsConnection — FinsClient 封装

```csharp
using System.Net;
using CableRobot.Fins;

namespace fins.driver;

public class FinsConnection : IDisposable
{
    private FinsClient? _client;
    private readonly string _remoteIp;
    private readonly int _port;
    private readonly int _timeoutMs;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public event Action<Exception, string>? OnError;

    public FinsConnection(string remoteIp, int port = 9600, int timeoutMs = 2000)
    {
        _remoteIp = remoteIp;
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public void Connect()
    {
        if (_client != null)
        {
            _client.Close();
            _client.Dispose();
        }

        var endpoint = new IPEndPoint(IPAddress.Parse(_remoteIp), _port);
        _client = new FinsClient(endpoint);
        _client.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);
    }

    // ct 仅控制信号量等待。读操作超时由 FinsClient.Timeout 控制。
    public async Task<ushort[]> ReadDAsync(ushort startAddress, ushort count, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Not connected");

        await _lock.WaitAsync(ct);
        try
        {
            return await _client.ReadDataAsync(startAddress, count);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ct 仅控制信号量等待。写操作超时由 FinsClient.Timeout 控制。
    public async Task WriteDAsync(ushort startAddress, ushort[] data, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Not connected");

        await _lock.WaitAsync(ct);
        try
        {
            await _client.WriteDataAsync(startAddress, data);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 先释放客户端，再释放信号量（缩小竞态窗口）
        _client?.Close();
        _client?.Dispose();
        _client = null;
        _lock.Dispose();
    }
}
```

### 3.3 FinsStrategy

```csharp
using fins.driver.models;

namespace fins.driver.strategies;

public class FinsStrategy : Strategy
{
    private FinsConnection? _connection;
    private readonly FinsConfig _config;
    private bool _reconnecting;

    // 数据直出：不经过 Handler。原因：FINS 数据为原始 ushort[]
    public event Action<string, ushort[]>? OnData;
    public event Action<Exception, string>? OnError;

    public FinsStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = FinsConfig.Parse(rawConfig);
    }

    public override async Task InitializeAsync()
    {
        _connection = new FinsConnection(
            _config.RemoteIp, _config.Port, _config.TimeoutMs);
        _connection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);

        try
        {
            _connection.Connect();
            IsHealthy = true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "InitializeAsync.Connect");
            IsHealthy = false;
            // 连接失败不抛出异常，允许 SweepAsync 中重试
        }
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        // 断线自动重连
        if (_connection == null || !_connection.IsConnected)
        {
            TryReconnect();
            if (!_connection?.IsConnected ?? true)
            {
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
                OnError?.Invoke(ex, $"Sweep collector={collector.Name}");
                allSuccess = false;
            }
        }

        LastSuccess = allSuccess;
        IsHealthy = allSuccess;
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
            _connection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);
            _connection.Connect();
            IsHealthy = true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "TryReconnect");
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

    public void DisposeConnection() => _connection?.Dispose();
}
```

### 3.4 FinsMachine

```csharp
namespace fins.driver;

public class FinsMachine : Machine
{
    public FinsMachine(Machines machines, object configuration)
        : base(machines, configuration) { }
}
```

### 3.5 配置模型

```csharp
// models/FinsCollectorConfig.cs
namespace fins.driver.models;

public class FinsCollectorConfig
{
    public string Name { get; set; } = "";
    public ushort StartAddress { get; set; }
    public ushort Length { get; set; }
}

// models/FinsConfig.cs
namespace fins.driver.models;

public class FinsConfig
{
    public string RemoteIp { get; set; } = "";
    public int Port { get; set; } = 9600;
    public int TimeoutMs { get; set; } = 2000;
    public FinsCollectorConfig[] Collectors { get; set; } = Array.Empty<FinsCollectorConfig>();

    public static FinsConfig Parse(dynamic rawConfig)
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
}
```

### 3.6 启动入口

推荐通过 YAML + `Machines.CreateMachines()` 启动。编程式入口参考：

```csharp
// Program.cs — 宿主启动
var config = new ConfigurationBuilder()
    .AddYamlFile("config.machines.yml")
    .Build();

var machines = await Machines.CreateMachines(config);
await machines.RunAsync(stoppingToken);
```

`Machines` 基类会按 YAML 配置自动创建 `FinsStrategy` → 调用 `InitializeAsync` → 循环 `RunStrategyAsync`（即 `SweepAsync`）。

---

## 4. YAML Configuration

```yaml
machines:
  - id: stacker_1
    enabled: true
    type: fins.driver.FinsMachine, fins-driver
    strategy: fins.driver.strategies.FinsStrategy, fins-driver
    handler: l99.driver.@base.Handler, base-driver

    fins.driver.FinsMachine, fins-driver:
      sweep_ms: 500

    fins.driver.strategies.FinsStrategy, fins-driver:
      remote_ip: "192.168.250.1"
      port: 9600
      timeout_ms: 2000

      collectors:
        - name: slot_signals
          start_address: 100
          length: 280

        - name: scanner_signals
          start_address: 10
          length: 2
```

---

## 5. Data Flow

```
SweepAsync tick
  → FinsConnection.ReadDAsync(address, count)
    → FinsClient.ReadDataAsync (NuGet 包处理 UDP 通信)
    → 返回 ushort[] 数据
    → FinsStrategy.OnData(collectorName, ushort[])
      → 宿主
```

---

## 6. Error Handling

| Scenario | Behavior |
|---|---|
| UDP 无响应 | FinsClient 超时（timeout_ms 配置）→ OnError + 继续下一 collector |
| 网络不可达 | FinsClient 异常 → OnError + 继续下一 collector |
| FINS 响应码非 0 | FinsClient 抛出 FinsException → OnError + 继续下一 collector |
| UDP 响应不验证源地址 | 由 NuGet 包内部处理。建议在隔离工业网络中使用 |

---

## 7. Non-Goals

- **其他内存区** — 只支持 `ReadData`/`WriteData`（D 寄存器）。`ReadWork`/`WriteWork`（W 寄存器）可通过 NuGet 包的同名方法扩展。
- **心跳/保活** — `FinsClient` 内部处理超时重试。宿主的 Sweep 循环本身起保活作用。
- **自动节点发现** — 不实现 FINS 网络广播发现。

---

## 8. Testing Strategy

| Component | Focus | Approach |
|---|---|---|
| `FinsConfig` | YAML 反序列化 | YamlDotNet test |
| `FinsConnection` | 连接/读取/写入 | Mocked FinsClient |
| `FinsStrategy` | SweepAsync dispatch | Mocked FinsConnection |
| End-to-end | 完整管线 | FINS UDP test server (simulated) |
