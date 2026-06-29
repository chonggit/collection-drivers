# DriverHostService — Unified Multi-Driver Host

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Introduce a single `DriverHostService` in `CollectionDrivers.Common` that replaces the four per-driver `BackgroundService` entry points. The host loads all driver configurations from a single `config.machines.yml` via `Machines.CreateMachines()`, then runs all machines concurrently in one process.

**Target project location:** `CollectionDrivers.Common/DriverHostService.cs`

---

## 2. DriverHostService

```csharp
using Microsoft.Extensions.Hosting;

namespace CollectionDrivers.Common;

public class DriverHostService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var yamlConfigPath = "config.machines.yml";

        var envPath = Environment.GetEnvironmentVariable("COLLECTION_DRIVERS_CONFIG");
        if (!string.IsNullOrEmpty(envPath))
            yamlConfigPath = envPath;

        // YAML 文件缺失或格式错误 — fail-fast，反馈即止
        var yaml = await File.ReadAllTextAsync(yamlConfigPath, stoppingToken);

        var deserializer = new DeserializerBuilder().Build();
        var parser = new Parser(new StringReader(yaml));
        var mergingParser = new MergingParser(parser);
        var config = deserializer.Deserialize(mergingParser);

        var machines = await Machines.CreateMachines(config);
        await machines.RunAsync(stoppingToken);
    }
}
```

### 2.1 CollectionDrivers.Common 依赖变更

```xml
<!-- CollectionDrivers.Common.csproj 新增 -->
<ItemGroup>
  <ProjectReference Include="..\base-driver\base-driver.csproj" />
</ItemGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
  <PackageReference Include="YamlDotNet" Version="16.3.0" />
</ItemGroup>
```

### 2.2 宿主启动

```csharp
// Program.cs — 只需一个 service
services.AddHostedService<DriverHostService>();
```

> 若 YAML 文件不在输出目录，需在 csproj 中添加：
> ```xml
> <ItemGroup>
>   <Content Include="config.machines.yml" CopyToOutputDirectory="PreserveNewest" />
> </ItemGroup>
> ```

### 2.3 统一 YAML 配置

```yaml
machines:
  - id: plc_opcua
    enabled: true
    type: opcua.driver.OpcUaMachine, opcua-driver
    strategy: opcua.driver.strategies.OpcUaStrategy, opcua-driver
    handler: l99.driver.@base.Handler, base-driver
    opcua.driver.OpcUaMachine, opcua-driver:
      sweep_ms: 1000
    opcua.driver.strategies.OpcUaStrategy, opcua-driver:
      endpoint: "opc.tcp://192.168.1.100:4840"

  - id: fins_stacker
    enabled: true
    type: CollectionDrivers.FinsDriver.FinsMachine, CollectionDrivers.FinsDriver
    strategy: CollectionDrivers.FinsDriver.Strategies.FinsStrategy, CollectionDrivers.FinsDriver
    handler: l99.driver.@base.Handler, base-driver
    CollectionDrivers.FinsDriver.FinsMachine, CollectionDrivers.FinsDriver:
      sweep_ms: 500
    CollectionDrivers.FinsDriver.Strategies.FinsStrategy, CollectionDrivers.FinsDriver:
      remote_ip: "192.168.250.1"

  - id: battery_cabinet_1
    enabled: true
    type: CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver
    strategy: CollectionDrivers.BatteryDriver.Strategies.BatteryTcpStrategy, CollectionDrivers.BatteryDriver
    handler: CollectionDrivers.BatteryDriver.BatteryHandler, CollectionDrivers.BatteryDriver
    CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver:
      sweep_ms: 1000
    CollectionDrivers.BatteryDriver.Strategies.BatteryTcpStrategy, CollectionDrivers.BatteryDriver:
      port: 13000
      warning_port: 13100
      heartbeat_timeout_s: 60
```

---

## 3. Battery-Driver Compatibility Fixes

### 3.1 BatteryHandler — 单参构造

**Breaking Change**: 构造函数从 `(Machine machine, DataPublisher publisher)` 改为 `(Machine machine)`。

```csharp
public class BatteryHandler : Handler, IDisposable
{
    private readonly DataPublisher _publisher = new();

    public BatteryHandler(Machine machine) : base(machine) { }

    public void Publish(ChannelRealData data) => _publisher.Publish(data);
    public void Publish(AlarmData data) => _publisher.Publish(data);
    public void Publish(ResultData data) => _publisher.Publish(data);
    public void Publish(StatusData data) => _publisher.Publish(data);
    public void Publish(WarningData data) => _publisher.Publish(data);
    public void Publish(AckData data) => _publisher.Publish(data);
    public DataPublisher Publisher => _publisher;

    public void Dispose() => _publisher.Dispose();
}
```

### 3.2 BatteryTcpStrategy — 自初始化连接（含 warning_port + 资源清理）

```csharp
private TcpConnection? _connection;
private TcpConnection? _warningConnection;
private readonly PendingCommandManager _pendingCommands = new();
private bool _disposed;

public event Action<Exception, string>? OnError;

public override async Task InitializeAsync()
{
    var config = Machine.Configuration.strategy;
    var port = config.ContainsKey("port") ? (int)config["port"] : 13000;
    var warningPort = config.ContainsKey("warning_port") ? (int)config["warning_port"] : 13100;
    var heartbeatTimeout = config.ContainsKey("heartbeat_timeout_s")
        ? (int)config["heartbeat_timeout_s"] : 60;

    // 连接 PendingCommandManager 的错误通道
    // PendingCommandManager 构造时传入 OnError 回调
    // _pendingCommands = new PendingCommandManager((ex, ctx) => OnError?.Invoke(ex, ctx));

    _connection = new TcpConnection(port, heartbeatTimeout);
    _connection.OnDataReceived += OnRawDataReceived;
    await _connection.StartListeningAsync();

    try
    {
        _warningConnection = new TcpConnection(warningPort, heartbeatTimeout);
        _warningConnection.OnDataReceived += OnRawDataReceived;
        await _warningConnection.StartListeningAsync();
    }
    catch (Exception ex)
    {
        _warningConnection?.Dispose();
        _warningConnection = null;
        OnError?.Invoke(ex, $"Warning port {warningPort} unavailable");
    }
}

// SweepAsync 保持空安全
public override async Task SweepAsync(int delayMs = -1)
{
    await Task.Delay(delayMs < 0 ? SweepMs : delayMs);
    _connection?.CheckHeartbeat();
    LastSuccess = _connection?.IsConnected ?? false;
    IsHealthy = _connection?.IsConnected ?? false;
    if (Machine?.Handler != null)
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
}

// 资源释放
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    _pendingCommands.Dispose();
    _warningConnection?.Dispose();
    _connection?.Dispose();
}
```

### 3.3 BatteryDriverService 公共 API 迁移

| 原 API | 所属 | 新位置 |
|---|---|---|
| `OnChannelData`, `OnAlarm`, `OnResult` 等事件 | 宿主订阅数据事件 | `BatteryHandler.Publisher.OnXxx` |
| `OnError` | 错误通知 | `BatteryTcpStrategy.OnError` |
| `StartFormationAsync(TurnOrder)` | 下发化成命令 | `BatteryTcpStrategy.SendCommandAsync()` |
| `PauseFormationAsync(byte, byte)` | 暂停命令 | `BatteryTcpStrategy.SendCommandAsync()` |
| `ResumeFormationAsync(byte, byte)` | 恢复命令 | `BatteryTcpStrategy.SendCommandAsync()` |
| `GetStatus()` | 状态查询 | `Machine.StrategyHealthy` + `TcpConnection.IsConnected` |
| `PendingCommandManager` | 命令 ACK 跟踪 | `BatteryTcpStrategy` 内部 |

### 3.4 运行时逐机容错

`Machines.RunMachineAsync` 循环添加 `try-catch` 保护，单次 Sweep 异常不停止整台机器：

```csharp
// Machines.RunMachineAsync — 新增容错
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await machine.RunStrategyAsync();
    }
    catch (OperationCanceledException)
    {
        break; // 正常关闭，让循环后的 Stop() 执行
    }
    catch (Exception ex)
    {
        // 日志 + 退避重试
        await Task.Delay(1000);
    }
}
```

### 3.5 BatteryDriverService 移除

`BatteryDriverService.cs` 由 `DriverHostService` 取代。

---

## 4. Per-Driver BackgroundService 清理

| 文件 | 状态 | 说明 |
|---|---|---|
| `CollectionDrivers.Common/DriverHostService.cs` | + 新增 | 统一入口 |
| `battery-driver/BatteryDriverService.cs` | - 移除 | 迁移至各组件 |
| `opcua-driver/OpcUaDriverService.cs` | - 移除 | 已 `throw NotImplementedException` |
| `fins-driver/FinsDriverService.cs` | - 移除 | 已 `throw NotImplementedException` |
| `scanner-driver/ScannerDriverService.cs` | - 移除 | 已 `throw NotImplementedException` |

---

## 5. Startup Comparison

```
之前: 4x services.AddHostedService<...>();
之后: services.AddHostedService<DriverHostService>();
```
