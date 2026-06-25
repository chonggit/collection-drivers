# OPC UA Driver Design

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Build a **generic** OPC UA client driver for the collection-drivers platform, following the base-driver architecture (`Machine → Strategy → Handler` pattern). The driver connects to any OPC UA server, supports both subscription-based event-driven data collection and polling-based periodic collection, and delivers structured key-value data to the host application through events.

**Target project location:** `collection-drivers/opcua-driver/`

**Key design principle:** Generic — not tied to any specific OPC UA server schema. Node configuration is entirely YAML-driven.

**OPC UA SDK:** Direct use of OPC Foundation official NuGet package `Opc.Ua.Client` (1.5.x), no intermediate wrapper.

---

## 2. Project Structure

```
collection-drivers/
├── base-driver/                  ← 已有框架
├── battery-driver/               ← 已有实现
├── opcua-driver/                 ← 新
│   ├── strategies/
│   │   └── OpcUaStrategy.cs      ← 订阅 + 轮询双模式
│   ├── OpcUaMachine.cs           ← 继承 Machine
│   ├── OpcUaDriverService.cs     ← BackgroundService 入口
│   ├── models/
│   │   ├── OpcUaConfig.cs        ← YAML 配置模型
│   │   └── CollectorConfig.cs    ← 采集器配置模型
│   └── opcua-driver.csproj
├── opcua-driver.test/
├── examples/
│   └── config.opcua.yml
└── collection-drivers.sln
```

### 2.1 Key Design Decisions

- **双模式并行**：同一 Session 上同时运行 subscription 和 poll 模式的 collector。订阅优先，轮询兜底。
- **数据直出**：Strategy 的 `OnData` 事件直达宿主，不经过 Handler。
- **框架兼容**：OpcUaStrategy 构造函数只接受 `Machine` 参数（兼容 `Activator.CreateInstance(type, this)` 模式），配置通过 `Machine.Configuration.strategy` 读取。
- **引用 NuGet**：`Opc.Ua.Client` 1.5.x，利用 `Session.ReadAsync`（原生异步，支持 `CancellationToken`）。

---

## 3. Component Design

### 3.1 NuGet 依赖

```xml
<PackageReference Include="Opc.Ua.Client" Version="1.5.*" />
```

### 3.2 配置模型

```csharp
// models/NodeConfig.cs
namespace opcua.driver.models;

public class NodeConfig
{
    public string Id { get; set; } = "";
    public string? Alias { get; set; }
}

// models/CollectorConfig.cs
namespace opcua.driver.models;

public class CollectorConfig
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "subscription";
    public int SamplingIntervalMs { get; set; } = 100;
    public int? SweepIntervalMs { get; set; }
    public NodeConfig[] Nodes { get; set; } = Array.Empty<NodeConfig>();
}

// models/OpcUaConfig.cs
namespace opcua.driver.models;

public class OpcUaConfig
{
    public string Endpoint { get; set; } = "";
    public bool UseSecurity { get; set; } = false;
    public int ReconnectPeriodMs { get; set; } = 10000;
    public bool AutoAcceptCerts { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public CollectorConfig[] Collectors { get; set; } = Array.Empty<CollectorConfig>();
}
```

### 3.3 OpcUaStrategy

```csharp
using System.IO;
using l99.driver.@base;
using Opc.Ua;
using Opc.Ua.Client;
using opcua.driver.models;

namespace opcua.driver.strategies;

public class OpcUaStrategy : Strategy, IAsyncDisposable
{
    private readonly OpcUaConfig _config;
    private ApplicationConfiguration? _appConfig;
    private Session? _session;
    private SessionReconnectHandler? _reconnectHandler;
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly Dictionary<string, DateTime> _lastSweepTime = new();
    private CancellationTokenSource? _disposeCts;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public event Action<string, Dictionary<string, object>>? OnData;
    public event Action<Exception, string>? OnError;
    public event Action<bool, string>? OnConnectionState;

    // 框架兼容：构造函数只接受 Machine（Activator.CreateInstance 模式）
    // 配置通过 Machine.Configuration.strategy 读取
    public OpcUaStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = ParseConfig(rawConfig);
    }

    private static OpcUaConfig ParseConfig(dynamic rawConfig)
    {
        // 从 YAML 配置字典构建 OpcUaConfig 对象
        // 使用 YamlDotNet 或 hand-roll 映射
        var config = new OpcUaConfig();
        if (rawConfig == null) return config;

        if (rawConfig.ContainsKey("endpoint"))
            config.Endpoint = (string)rawConfig["endpoint"];
        if (rawConfig.ContainsKey("use_security"))
            config.UseSecurity = (bool)rawConfig["use_security"];
        if (rawConfig.ContainsKey("reconnect_period_ms"))
            config.ReconnectPeriodMs = (int)rawConfig["reconnect_period_ms"];
        if (rawConfig.ContainsKey("auto_accept_certs"))
            config.AutoAcceptCerts = (bool)rawConfig["auto_accept_certs"];
        if (rawConfig.ContainsKey("user_name"))
            config.UserName = (string?)rawConfig["user_name"];
        if (rawConfig.ContainsKey("password"))
            config.Password = (string?)rawConfig["password"];

        if (rawConfig.ContainsKey("collectors"))
        {
            var collectors = new List<CollectorConfig>();
            foreach (var c in rawConfig["collectors"])
            {
                var cc = new CollectorConfig
                {
                    Name = (string)c["name"],
                    Mode = (string)c["mode"],
                    SamplingIntervalMs = c.ContainsKey("sampling_interval_ms") ? (int)c["sampling_interval_ms"] : 100
                };
                if (c.ContainsKey("sweep_interval_ms"))
                    cc.SweepIntervalMs = (int)c["sweep_interval_ms"];
                if (c.ContainsKey("nodes"))
                {
                    var nodes = new List<NodeConfig>();
                    foreach (var n in c["nodes"])
                    {
                        nodes.Add(new NodeConfig
                        {
                            Id = (string)n["id"],
                            Alias = n.ContainsKey("alias") ? (string?)n["alias"] : null
                        });
                    }
                    cc.Nodes = nodes.ToArray();
                }
                collectors.Add(cc);
            }
            config.Collectors = collectors.ToArray();
        }

        return config;
    }

    // ================ 初始化 ================

    public override async Task InitializeAsync()
    {
        _disposeCts = new CancellationTokenSource();

        _appConfig = CreateApplicationConfig();

        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var endpoint = CoreClientUtils.SelectEndpoint(_config.Endpoint, _config.UseSecurity);
            if (endpoint == null)
                throw new Exception($"No OPC UA endpoint found at {_config.Endpoint}");

            var endpointConfig = EndpointConfiguration.Create(_appConfig);

            // 构造 UserIdentity
            IUserIdentity userIdentity;
            if (!string.IsNullOrEmpty(_config.UserName))
                userIdentity = new UserIdentity(_config.UserName, _config.Password ?? "");
            else
                userIdentity = new UserIdentity(new AnonymousIdentityToken());

            _session = await Session.Create(
                _appConfig,
                new ConfiguredEndpoint(endpoint, endpointConfig),
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "OpcUaDriver",
                sessionTimeout: 60000u,
                identity: userIdentity,
                preferredLocales: null,
                cancellationToken: connectCts.Token);

            _session.KeepAlive += OnKeepAlive;
            OnConnectionState?.Invoke(true, "Connected");

            // 校验 collector 配置
            foreach (var collector in _config.Collectors)
            {
                if (collector.Mode != "subscription" && collector.Mode != "poll")
                    OnError?.Invoke(
                        new InvalidDataException($"Unknown collector mode '{collector.Mode}' for '{collector.Name}'"),
                        "InitializeAsync");
            }
            // 检测重复名称
            var duplicateNames = _config.Collectors
                .GroupBy(c => c.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var name in duplicateNames)
                OnError?.Invoke(
                    new InvalidDataException($"Duplicate collector name '{name}'"),
                    "InitializeAsync");

            // 创建订阅
            foreach (var collector in _config.Collectors.Where(c => c.Mode == "subscription" && c.Nodes.Length > 0))
            {
                CreateSubscription(collector);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "InitializeAsync");
            throw;
        }
    }

    private ApplicationConfiguration CreateApplicationConfig()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "OpcUaDriver",
            ApplicationType = ApplicationType.Client,
            CertificateValidator = new CertificateValidator
            {
                AutoAcceptUntrustedCertificates = _config.AutoAcceptCerts
            },
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = _config.AutoAcceptCerts,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 30000,
                MaxStringLength = int.MaxValue,
                MaxArrayLength = 65535,
                MaxMessageSize = 419430400,
                MaxBufferSize = 65535
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000,
                MinSubscriptionLifetime = 60000
            }
        };

        config.CertificateValidator.CertificateValidation += (_, eventArgs) =>
        {
            if (ServiceResult.IsGood(eventArgs.Error))
                eventArgs.Accept = true;
            else if (eventArgs.Error.StatusCode.Code == 0x80000000u)
                eventArgs.Accept = _config.AutoAcceptCerts;
        };

        return config;
    }

    private void CreateSubscription(CollectorConfig collector)
    {
        if (_session == null) return;

        var sub = new Subscription(_session.DefaultSubscription)
        {
            PublishingInterval = collector.SamplingIntervalMs,
            DisplayName = collector.Name,
            PublishingEnabled = true
        };

        foreach (var node in collector.Nodes)
        {
            NodeId nodeId;
            try
            {
                nodeId = new NodeId(node.Id);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, $"Invalid nodeId: {node.Id}");
                continue;
            }

            var item = new MonitoredItem
            {
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                DisplayName = node.Alias ?? node.Id,
                SamplingInterval = collector.SamplingIntervalMs,
                QueueSize = 1,
                DiscardOldest = true
            };

            item.Notification += (monitoredItem, args) =>
            {
                foreach (var notification in monitoredItem.DequeueUpdates())
                {
                    if (notification is not MonitoredItemNotification min)
                        continue;

                    // 检查数据质量（StatusCode）
                    if (min.Value != null && ServiceResult.IsBad(min.Value.StatusCode))
                    {
                        OnError?.Invoke(
                            new InvalidDataException($"Bad quality: {min.Value.StatusCode} for {monitoredItem.DisplayName}"),
                            $"Subscription.{collector.Name}");
                        continue;
                    }

                    var rawValue = min.Value?.Value;
                    if (rawValue == null)
                    {
                        OnError?.Invoke(
                            new InvalidDataException($"Null value for node {monitoredItem.DisplayName}"),
                            $"Subscription.{collector.Name}");
                        continue;
                    }

                    var dict = new Dictionary<string, object>
                    {
                        [monitoredItem.DisplayName] = rawValue
                    };
                    OnData?.Invoke(collector.Name, dict);
                }
            };
            sub.AddItem(item);
        }

        if (sub.MonitoredItemCount > 0)
        {
            try
            {
                _session.AddSubscription(sub);
                sub.Create();
                _subscriptions[collector.Name] = sub;
            }
            catch (Exception ex)
            {
                // sub.Create 失败时回滚：从 Session 移除并释放
                try { _session.RemoveSubscription(sub); } catch { }
                sub.Dispose();
                OnError?.Invoke(ex, $"CreateSubscription.{collector.Name}");
            }
        }
        else
        {
            sub.Dispose(); // 没有有效节点时释放订阅对象
        }
    }

    // ================ Sweep（轮询模式 + 保活） ================

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        if (_session == null || !_session.Connected)
        {
            // 无论连接状态，都执行框架生命周期钩子
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
            return;
        }

        foreach (var collector in _config.Collectors.Where(c => c.Mode == "poll" && c.Nodes.Length > 0))
        {
            var lastSweep = _lastSweepTime.GetValueOrDefault(collector.Name, DateTime.MinValue);
            var interval = collector.SweepIntervalMs ?? 5000;
            if ((DateTime.UtcNow - lastSweep).TotalMilliseconds < interval)
                continue;

            _lastSweepTime[collector.Name] = DateTime.UtcNow;

            try
            {
                // 逐节点构建 NodeId，异常隔离
                var validNodes = new List<(NodeConfig config, NodeId nodeId)>();
                foreach (var n in collector.Nodes)
                {
                    try
                    {
                        validNodes.Add((n, new NodeId(n.Id)));
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(ex, $"Invalid nodeId: {n.Id} in collector {collector.Name}");
                    }
                }

                if (validNodes.Count == 0) continue;

                var nodesToRead = new ReadValueIdCollection(
                    validNodes.Select(v => new ReadValueId
                    {
                        NodeId = v.nodeId,
                        AttributeId = Attributes.Value
                    }));

                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                // ReadAsync 签名：ReadAsync(double maxAge, TimestampsToReturn, ReadValueIdCollection, CancellationToken)
                var results = await _session.ReadAsync(
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    readCts.Token);

                var dict = new Dictionary<string, object>();
                for (int i = 0; i < validNodes.Count && i < results.Count; i++)
                {
                    var key = validNodes[i].config.Alias ?? validNodes[i].config.Id;

                    // 检查 StatusCode
                    if (results[i] != null && ServiceResult.IsBad(results[i].StatusCode))
                    {
                        OnError?.Invoke(
                            new InvalidDataException($"Bad quality: {results[i].StatusCode} for {key}"),
                            $"Sweep.collector={collector.Name}");
                        continue;
                    }

                    var value = results[i]?.Value?.Value;
                    if (value != null)
                        dict[key] = value;
                    else
                        OnError?.Invoke(
                            new InvalidDataException($"Null value for node {key}"),
                            $"Sweep.collector={collector.Name}");
                }
                if (dict.Count > 0)
                    OnData?.Invoke(collector.Name, dict);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, $"Sweep collector={collector.Name}");
            }
        }

        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
        // 仅框架生命周期钩子，不参与数据路径
    }

    // ================ KeepAlive & 重连（含订阅重建） ================

    private void OnKeepAlive(Session sender, KeepAliveEventArgs e)
    {
        if (ServiceResult.IsGood(e.Status))
            return;

        OnError?.Invoke(new Exception($"KeepAlive failed: {e.Status}"), "KeepAlive");
        OnConnectionState?.Invoke(false, "Disconnected");
        _ = ReconnectAndRestoreAsync();
    }

    private async Task ReconnectAndRestoreAsync()
    {
        if (!await _reconnectLock.WaitAsync(0))
            return;

        try
        {
            // 如果已被 Dispose，不再重连
            if (_disposeCts?.IsCancellationRequested == true)
                return;

            _reconnectHandler?.Dispose();
            _reconnectHandler = new SessionReconnectHandler();

            var tcs = new TaskCompletionSource<bool>();
            _reconnectHandler.BeginReconnect(
                _session!,
                _config.ReconnectPeriodMs,
                (_, e) =>
                {
                    // 检查重连结果状态
                    if (ServiceResult.IsGood(e.Status))
                        tcs.TrySetResult(true);
                    else
                        tcs.TrySetException(new Exception($"Reconnect failed: {e.Status}"));
                });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _disposeCts!.Token);
            if (await Task.WhenAny(tcs.Task, Task.Delay(-1, linkedCts.Token)) != tcs.Task)
            {
                OnError?.Invoke(new TimeoutException("Reconnect timeout"), "ReconnectAsync");
                return;
            }

            // 移除旧订阅（避免 Session 内堆积）
            foreach (var kvp in _subscriptions)
            {
                try
                {
                    _session?.RemoveSubscription(kvp.Value);
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _subscriptions.Clear();

            // 重建所有订阅
            foreach (var collector in _config.Collectors.Where(c => c.Mode == "subscription"))
            {
                CreateSubscription(collector);
            }

            OnConnectionState?.Invoke(true, "Reconnected");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "ReconnectAsync");
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    // ================ 资源释放 ================

    public async ValueTask DisposeAsync()
    {
        _disposeCts?.Cancel();

        // 等待正在进行的重连完成
        await _reconnectLock.WaitAsync();
        try
        {
            foreach (var kvp in _subscriptions)
            {
                try
                {
                    _session?.RemoveSubscription(kvp.Value);
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _subscriptions.Clear();
        }
        finally
        {
            _reconnectLock.Release();
        }

        _reconnectHandler?.Dispose();
        _reconnectHandler = null;

        if (_session != null)
        {
            try { await _session.CloseAsync(); } catch { }
            _session.Dispose();
            _session = null;
        }

        _disposeCts?.Dispose();
        OnConnectionState?.Invoke(false, "Disposed");
    }

    public bool IsConnected => _session?.Connected ?? false;
}
```

### 3.4 OpcUaMachine

```csharp
using l99.driver.@base;

namespace opcua.driver;

public class OpcUaMachine : Machine
{
    public OpcUaMachine(Machines machines, object configuration) : base(machines, configuration)
    {
    }
}
```

### 3.5 OpcUaDriverService

可选入口点，与 YAML 管线相互独立。不通过 YAML 时直接编程使用：

```csharp
using System.IO;
using Microsoft.Extensions.Hosting;
using opcua.driver.models;
using opcua.driver.strategies;
using l99.driver.@base;

namespace opcua.driver;

public class OpcUaDriverService : BackgroundService
{
    private readonly OpcUaConfig _config;
    private OpcUaStrategy? _strategy;

    public event Action<string, Dictionary<string, object>>? OnData;
    public event Action<Exception, string>? OnError;

    // 编程式入口：通过构造函数注入配置（非 YAML 场景）
    public OpcUaDriverService(OpcUaConfig config)
    {
        _config = config;
    }

    // 默认构造（仅用于 DI 容器占位，实际推荐通过 YAML + Machines.CreateMachines() 启动）
    // 注意：此 Service 主要用于编程模式；YAML 模式下由框架直接创建 Machine/Strategy
    public OpcUaDriverService()
    {
        _config = new OpcUaConfig();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var machine = CreateMinimalMachine();
        _strategy = new OpcUaStrategy(machine);
        // 将外部传入的配置注入 Strategy（框架模式下由 YAML 配置替代）
        _strategy.OnData += (group, data) => OnData?.Invoke(group, data);
        _strategy.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);

        await _strategy.InitializeAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await _strategy.SweepAsync();
        }
    }

    private Machine CreateMinimalMachine()
    {
        // 编程模式下创建一个最小 Machine 实例以满足 Strategy 构造要求
        // 实际 YAML 路径下由 Machines.CreateMachines() 创建
        throw new NotImplementedException("Use YAML configuration path for full setup");
    }

    internal OpcUaStrategy? Strategy => _strategy;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 先等待 ExecuteAsync 退出，再释放资源
        await base.StopAsync(cancellationToken);
        if (_strategy != null)
            await _strategy.DisposeAsync();
    }
}
```

> **使用路径**：
> - **推荐**：YAML + `Machines.CreateMachines()` 启动，OPC UA 配置从 `config.machines.yml` 读取。
> - **编程式**：`services.AddSingleton(new OpcUaConfig { ... }); services.AddHostedService<OpcUaDriverService>();`

---

## 4. YAML Configuration

```yaml
machines:
  - id: plc_line1
    enabled: true
    type: opcua.driver.OpcUaMachine, opcua-driver
    strategy: opcua.driver.strategies.OpcUaStrategy, opcua-driver
    handler: l99.driver.@base.Handler, base-driver

    # 机器类型配置段（框架要求：提供 sweep_ms）
    opcua.driver.OpcUaMachine, opcua-driver:
      sweep_ms: 1000

    # 策略配置段
    opcua.driver.strategies.OpcUaStrategy, opcua-driver:
      endpoint: "opc.tcp://192.168.1.100:4840"
      use_security: false
      reconnect_period_ms: 10000
      auto_accept_certs: true
      # 实际 Sweep 间隔由基类 Strategy 从 machine 段 "type.sweep_ms" 中读取

      collectors:
        - name: machine_status
          mode: subscription
          sampling_interval_ms: 100
          nodes:
            - id: "ns=2;s=Machine.Running"
              alias: running
            - id: "ns=2;s=Machine.Temperature"
              alias: temperature

        - name: production_counters
          mode: poll
          sweep_interval_ms: 5000
          nodes:
            - id: "ns=2;s=Line1.GoodCount"
              alias: good_count
            - id: "ns=2;s=Line1.BadCount"
              alias: bad_count
```

---

## 5. Data Flow

### Subscription Mode
```
OPC UA Server 推送
  → MonitoredItem.DequeueUpdates()
    → OpcUaStrategy.OnData 事件 (group, dict)
      → 宿主
```

### Poll Mode
```
SweepAsync tick
  → _session.ReadAsync() (官方 SDK 异步批量读)
    → 组装 Dictionary<string, object>
    → OpcUaStrategy.OnData 事件 (group, dict)
      → 宿主
```

---

## 6. Error Handling

| Scenario | Behavior |
|---|---|
| Server 断开 | KeepAlive 检测 → `OnConnectionState(false)` + `ReconnectAndRestoreAsync` |
| 节点不可读 | Read 返回 null → `OnError` + 不加入 dict |
| 订阅失效 | 重连后 `ReconnectAndRestoreAsync` 自动重建所有订阅 |
| KeepAlive 超时 | 触发断开 + 重连 |
| 证书无效 | AutoAcceptCerts=true 自动接受；false 则连接失败 |
| NodeId 格式无效 | OnError + Skip 该节点，其他节点不受影响 |
| 轮询超时 | 10s CancellationToken → OnError |
| Session.Create 超时 | 30s CancellationToken → OnError + 抛出 |
| Endpoint 找不到 | OnError + 抛出 |

---

## 7. Concurrency

- `OnData` 事件在 SDK 线程池（订阅回调）和 SweepAsync 主循环上交替触发
- 宿主**不应假设**事件在同一线程触发
- 如需线程安全聚合，宿主使用 `lock` 或 `ConcurrentDictionary`

---

## 8. Non-Goals

- OPC UA 方法调用 / 历史数据 / Browse 节点发现
- 类型强转 — 输出原始值
- 生命周期内注册的类型别名（后续可加）

---

## 9. Testing Strategy

| Component | Focus | Approach |
|---|---|---|
| `OpcUaStrategy` | 框架兼容构造 + 配置解析 | Unit test with mock Machine |
| `OpcUaStrategy` | 订阅/轮询调度 | Mock Session |
| `OpcUaStrategy` | DisposeAsync 资源释放 | Verify Session.CloseAsync called |
| `OpcUaStrategy` | 重连：KeepAlive → ReconnectAndRestoreAsync | Mock KeepAlive event, verify reconnect lock acquired |
| `OpcUaStrategy` | 重连成功 → 订阅重建完成 | Verify old subscriptions removed + new created |
| `OpcUaStrategy` | 重连超时 → OnError | Short timeoutCts |
| `OpcUaStrategy` | 连续重连 → _reconnectLock 互斥 | Fire KeepAlive twice, verify single reconnect |
| `OpcUaStrategy` | 重连期间 Dispose → 无竞态 | Race test with Task.Delay |
| `OpcUaConfig` | YAML 反序列化 | YamlDotNet deserialization test |
| End-to-end | 完整管线 | OPC UA simulation server |
