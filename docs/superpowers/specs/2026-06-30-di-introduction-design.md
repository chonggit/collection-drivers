# DI 引入设计文档

## 概述

将 collection-drivers 从反射 + 静态定位器模式迁移到 Microsoft.Extensions.DependencyInjection，
实现类型安全的依赖注入，消除 `Activator.CreateInstance`、`dynamic` 配置、`LoggingFactory` 等反模式。

## 目标

- **完整 DI 化**：提升可测试性 + 改善架构解耦
- **增量共存迁移**：分 5 个 Phase，每 Phase 独立 PR
- **支持持续新增驱动**：显式注册模式 + 保留扩展点
- **类库模式**：提供 `IServiceCollection` 扩展，由宿主控制注册
- **配置标准化**：从 `dynamic` 迁移到强类型 Options
- **测试分层**：Mock 单元测试 + 集成测试共存

---

## 架构设计

### 当前架构问题

```
DriverHostService (读 YAML → dynamic)
  └── Machines.CreateMachines(config)
        └── Activator.CreateInstance(machineType, config)
              └── Machine.AddStrategyAsync(strategyType)
                    └── Activator.CreateInstance(strategyType, this)
              └── Machine.AddHandlerAsync(handlerType)
                    └── Activator.CreateInstance(handlerType, this)
              └── Machine.AddTransportAsync(transportType)
                    └── Activator.CreateInstance(transportType, this)
```

问题：
1. 反射创建，无编译时检查
2. `dynamic` 配置透传，无类型安全
3. `LoggingFactory` 静态定位器
4. Machine ↔ Strategy 循环引用
5. 无法 Mock 依赖进行单元测试

### 目标架构

```
宿主 Program.cs:
  IConfiguration
    ├── .GetSection("CollectionDrivers") → AddCollectionDrivers(section)
    │     ├── 注册 DriverHostService (IHostedService)
    │     ├── 注册 IMachineScopeFactory (Singleton)
    │     └── services.Configure<CollectionDriverOptions>(section)
    ├── AddBatteryDriver() → 注册驱动类型元数据到 DriverTypeRegistry
    └── AddInfluxDbTransport() → 注册 Transport 类型元数据

DriverHostService (注入 IConfiguration + IOptions<CollectionDriverOptions> + IMachineScopeFactory):
  ExecuteAsync:
    ├── 读 _options.Value.Machines
    ├── 为每台机器: 用 _config 填充 machineCfg.Configuration 引用
    └── foreach machineCfg (Enabled=true):
          IMachineScopeFactory.CreateScope(machineCfg)
            ├── 创建 IServiceScope
            ├── 查找 DriverTypeRegistry 中匹配的 Entry
            ├── 绑定 Strategy/Transport Options
            ├── ActivatorUtilities 构造 Machine → Strategy → Handler → Transport
            ├── 将组件回挂到 Machine (属性注入)
            └── RunAsync: InitializeAsync → SweepAsync 循环
```

核心设计：**DI 负责基础设施（ILogger），ActivatorUtilities 负责组件组装**。

---

## 详细设计

### 第 1 节：配置层 — 消除 `dynamic`

**现状**：YAML → YamlDotNet → `dynamic` 对象透传。每个组件自己从 `dynamic` 取值，无编译时检查。

**设计**：驱动库不再解析 YAML/JSON。宿主通过 `IConfiguration` 传入配置。

#### 1.1 顶层配置 DTO

```csharp
/// <summary>驱动库顶层配置，从宿主 IConfiguration 绑定</summary>
public class CollectionDriverOptions
{
    /// <summary>机器列表</summary>
    public List<MachineOptions> Machines { get; set; } = new();
}

/// <summary>单台机器的配置</summary>
public class MachineOptions
{
    /// <summary>机器标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Machine 具体类型全名。
    /// 若为 null 则默认使用 Machine 基类（推荐）。
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 驱动标识符。与 Add*Driver 注册时的 DriverId 对应。
    /// 如 "Battery"、"Fins"、"OpcUa"、"Scanner"。
    /// 多驱动共存时用于区分不同驱动的类型注册条目。
    /// </summary>
    public string? DriverId { get; set; }

    /// <summary>采集间隔（毫秒），对应旧 type.sweep_ms</summary>
    public int SweepMs { get; set; } = 5000;

    /// <summary>
    /// 当前机器对应的 IConfiguration 段引用。
    /// ⚠️ 不通过 .Bind() 填充——由 DriverHostService 在运行时手动注入。
    /// DriverHostService 持有 IConfiguration，遍历 Machines 列表时按索引
    /// 从 $"{rootSection}:Machines:{index}" 获取子段。
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public IConfiguration? Configuration { get; set; }
}
```

**填充机制**（`DriverHostService.ExecuteAsync`）：

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    var machines = _options.Value.Machines.Where(m => m.Enabled).ToList();
    var rootSection = _config.GetSection("CollectionDrivers");

    for (int i = 0; i < _options.Value.Machines.Count; i++)
    {
        var machineCfg = _options.Value.Machines[i];
        if (!machineCfg.Enabled) continue;

        // 填入 IConfiguration 子段引用
        machineCfg.Configuration = rootSection.GetSection($"Machines:{i}");
        // ... 创建 scope
    }
}
```

> `CollectionDriverOptions` 绑定忽略 `Configuration` 属性（通过 `[JsonIgnore]` 标记）。
> 该属性仅作为运行时的引用传递通道，不参与序列化/反序列化。

#### 1.2 各驱动配置 DTO

```csharp
// BatteryDriver 项目内
public class BatteryTcpStrategyOptions
{
    public int Port { get; set; } = 13000;
    public int WarningPort { get; set; } = 13100;
    public int HeartbeatTimeoutS { get; set; } = 60;
}

// Transport.InfluxDB 项目内
public class InfluxDbTransportOptions
{
    public string Host { get; set; } = "http://localhost:8086";
    public string Token { get; set; } = "";
    public string Bucket { get; set; } = "default";
    public string Org { get; set; } = "default";
    public Dictionary<string, string> Transformers { get; set; } = new();
}
```

#### 1.3 宿主配置示例（appsettings.json）

```json
{
  "CollectionDrivers": {
    "Machines": [
      {
        "Id": "battery-line1",
        "Enabled": true,
        "Type": null,
        "DriverId": "Battery",
        "SweepMs": 5000,
        "Strategy": { "Port": 13000, "WarningPort": 13100 },
        "Handler": {},
        "Transport": { "Host": "http://influx:8086", "Token": "...", "Bucket": "battery" }
      }
    ]
  }
}
```

**迁移期共存**：Phase 1 中旧 YAML → `dynamic` 路径继续工作。新 `IConfiguration` 路径独立运行。

#### 配置键名迁移注意事项

旧 YAML 配置使用 `snake_case` 键名（如 `sweep_ms`、`warning_port`）。
`IConfiguration.Bind()` 绑定到 PascalCase C# 属性时需键名匹配。
`IConfiguration` 大小写不敏感，但 `snake_case` 不会自动转换为 `PascalCase`（`sweep_ms` ≠ `SweepMs`）。
宿主迁移到 `appsettings.json` 时需将键名从 `snake_case` 改为 `PascalCase`。
Phase 1 建议在 Options 类上标注 `[ConfigurationKeyName("sweep_ms")]`（.NET 6+ 支持）兼容旧 YAML 的 `snake_case` 键名；
或由宿主在构建 `IConfiguration` 时使用自定义 `KeyNameMutator` 转换命名风格。

---

### 第 2 节：抽象层 — 接口与构造函数

#### 2.1 `IHandler` 接口（切断循环依赖的最小接口）

```csharp
/// <summary>采集完成后的数据处理契约</summary>
public interface IHandler
{
    /// <summary>采集周期完成时调用</summary>
    Task OnStrategySweepCompleteInternalAsync();
}
```

> 必须引入——`IMachineContext` 需要暴露 Handler 给 Strategy 调用。
> 与 YAGNI 不冲突：这是当前 DI 化的技术前提，不是"未来可能需要"的抽象。

#### 2.2 `IMachineContext` 接口

```csharp
/// <summary>Strategy/Handler/Transport 对 Machine 的只读视图</summary>
public interface IMachineContext
{
    string Id { get; }
    bool Enabled { get; }
    int SweepMs { get; }
    IHandler Handler { get; }
    IReadOnlyList<Transport> Transports { get; }

    /// <summary>Strategy 上次采集是否成功（来自 Strategy.LastSuccess）</summary>
    bool StrategySuccess { get; }

    /// <summary>Strategy 当前是否健康（来自 Strategy.IsHealthy）</summary>
    bool StrategyHealthy { get; }

    /// <summary>停止设备，运行中的采集循环由此退出</summary>
    Task Stop();
}
```

`Machine` 实现此接口。`StrategySuccess` 和 `StrategyHealthy` 转发到 `Strategy.LastSuccess` / `Strategy.IsHealthy`，
供 `TransportHandler` 构建 `SweepEndPayload`。

#### 2.3 构造函数 — 基类与子类分别设计

> **Phase 区分**：
> - Phase 1：`IHandler`、`IMachineContext` 接口已定义，但 `Machine` 尚未实现
> - Phase 2：新增 `(ILogger? logger, Machine machine)` 构造函数，接口已可用但生产代码暂用具体类
> - Phase 3：`Machine` 实现 `IMachineContext`，构造函数改为 `(ILogger? logger, IMachineContext context)`，旧构造函数标记 `[Obsolete]`
> - Phase 5：删除 `[Obsolete]` 旧构造函数
>
> 以下代码为 Phase 3 最终形态：

**Strategy**：

```csharp
// 基类（Common 项目）
public abstract class Strategy
{
    protected readonly ILogger Logger;
    protected readonly IMachineContext Context;
    protected readonly int SweepMs;

    [Obsolete("Use Strategy(ILogger?, IMachineContext) instead")]
    protected Strategy(Machine machine) : this(null, machine) { }

    protected Strategy(ILogger? logger, IMachineContext context)
    {
        Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
        Context = context;
        SweepMs = context.SweepMs;  // Machine 内部持有 SweepMs，由 MachineOptions 赋值
    }
}

// 子类示例（BatteryDriver 项目）
public class BatteryTcpStrategy : Strategy
{
    private readonly BatteryTcpStrategyOptions _options;

    [Obsolete]
    public BatteryTcpStrategy(Machine machine) : base(machine) { }

    /// <summary>DI 构造函数：ILogger + IMachineContext + 驱动专用 Options</summary>
    public BatteryTcpStrategy(
        ILogger? logger,
        IMachineContext context,
        BatteryTcpStrategyOptions options) : base(logger, context)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
}
```

**Handler**：

```csharp
public class Handler
{
    [Obsolete]
    protected Handler(Machine machine) : this(null, machine) { }

    protected Handler(ILogger? logger, IMachineContext context)
    {
        Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
        Context = context;
    }
}

// TransportHandler（唯一子类）需要显式转发构造函数
public class TransportHandler : Handler
{
    [Obsolete]
    public TransportHandler(Machine machine) : base(machine) { }

    public TransportHandler(ILogger? logger, IMachineContext context)
        : base(logger, context) { }

    // OnStrategySweepCompleteInternalAsync 实现中 Machine. → Context. 引用适配：
    //   Machine.Id → Context.Id
    //   Machine.StrategySuccess → Context.StrategySuccess
    //   Machine.StrategyHealthy → Context.StrategyHealthy
    //   Machine.Transports → Context.Transports
    // 逻辑骨架不变，仅访问路径从具体类改为接口
}
```

**Transport**：

```csharp
public class Transport
{
    [Obsolete]
    protected Transport(Machine machine) : this(null, machine) { }

    protected Transport(ILogger? logger, IMachineContext context)
    {
        Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
        Context = context;
    }
}

// Transport 子类
public class InfluxDbTransport : Transport
{
    private readonly InfluxDbTransportOptions _options;

    [Obsolete]
    public InfluxDbTransport(Machine machine) : base(machine) { }

    public InfluxDbTransport(
        ILogger? logger,
        IMachineContext context,
        InfluxDbTransportOptions options) : base(logger, context)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }
}
```

**Machine**：

```csharp
public class Machine : IMachineContext
{
    private readonly ILogger _logger;
    // 实现 IMachineContext...
}
```

| 类型 | 旧主构造函数 | Phase 2 过渡构造函数 | Phase 3+ 最终构造函数 |
|------|-------------|---------------------|----------------------|
| `Strategy` | `(Machine)` | `(ILogger?, Machine)` | `(ILogger?, IMachineContext)` |
| `BatteryTcpStrategy` | `(Machine)` | `(ILogger?, Machine, BatteryTcpStrategyOptions)` | `(ILogger?, IMachineContext, BatteryTcpStrategyOptions)` |
| `Handler` | `(Machine)` | `(ILogger?, Machine)` | `(ILogger?, IMachineContext)` |
| `TransportHandler` | `(Machine)` | `(ILogger?, Machine)` | `(ILogger?, IMachineContext)` |
| `Transport` | `(Machine)` | `(ILogger?, Machine)` | `(ILogger?, IMachineContext)` |
| `InfluxDbTransport` | `(Machine)` | `(ILogger?, Machine, InfluxDbTransportOptions)` | `(ILogger?, IMachineContext, InfluxDbTransportOptions)` |
| `Machine` | `(Machines, object)` | `(ILogger?)` + `Initialize(MachineOptions)` | 同左 |

#### 2.4 `Machine` 新增方法（替代旧的属性注入 + dynamic 配置）

```csharp
public class Machine : IMachineContext
{
    // ... 构造函数、IMachineContext 实现 ...

    /// <summary>用 MachineOptions 初始化 Machine 状态（替代构造函数中的 dynamic 配置）</summary>
    public void Initialize(MachineOptions options)
    {
        _sweepMs = options.SweepMs;
        _enabled = options.Enabled;
        _id = options.Id;  // 迁移期间 Id 优先用 _id，fallback 到 Configuration.machine.id
    }

    // Id 属性迁移策略（Phase 3）：
    // public string Id => _id ?? Configuration?.machine?.id ?? "";
    // 新路径（MachineScope）调用 Initialize 后 _id 非 null；
    // 旧路径（Machines.CreateMachines）不调用 Initialize，Id 走 Configuration 逻辑
    // Phase 5 删除 Configuration 后简化为 public string Id => _id;

    /// <summary>回挂 Strategy 实例（由 MachineScope 调用）</summary>
    internal void SetStrategy(Strategy strategy) => _strategy = strategy;

    /// <summary>回挂 Handler 实例</summary>
    internal void SetHandler(Handler handler) => _handler = handler;

    /// <summary>回挂 Transport 列表</summary>
    internal void SetTransports(List<Transport> transports) => _transports = transports;
}
```

> 这些方法是 `internal`，仅 `MachineScope`（同为 Common 项目内部）调用。
> 替代了旧的 `AddStrategyAsync(Type)` / `AddHandlerAsync(Type)` / `AddTransportAsync(Type)` 反射路径。

---

### 第 3 节：服务注册

#### 3.1 `IMachineScopeFactory` 接口

```csharp
/// <summary>创建 MachineScope 的工厂。由 DI 容器注册为 Singleton。</summary>
public interface IMachineScopeFactory
{
    /// <summary>为指定机器配置创建独立的采集 Scope</summary>
    IMachineScope CreateScope(MachineOptions config);
}
```

**实现**：`MachineScopeFactory` 注入 `IServiceScopeFactory` + `IEnumerable<DriverTypeRegistry.Entry>`，
在 `CreateScope` 中构建 `DriverTypeRegistry` 查找表并传递给 `MachineScope` 构造函数。

```csharp
internal class MachineScopeFactory : IMachineScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DriverTypeRegistry _registry;

    public MachineScopeFactory(
        IServiceScopeFactory scopeFactory,
        IEnumerable<DriverTypeRegistry.Entry> entries)
    {
        _scopeFactory = scopeFactory;
        _registry = new DriverTypeRegistry();
        foreach (var e in entries) _registry.Entries.Add(e);
    }

    public IMachineScope CreateScope(MachineOptions config)
    {
        var scope = _scopeFactory.CreateScope();
        return new MachineScope(scope, config, _registry);
    }
}
```

#### 3.2 `DriverTypeRegistry` 类型注册表

```csharp
/// <summary>
/// 驱动类型注册表（Singleton）。各驱动的 Add 扩展方法在此注册类型元数据。
/// MachineScope 创建时通过 MachineOptions.DriverId 查找匹配条目。
/// </summary>
public class DriverTypeRegistry
{
    public readonly List<Entry> Entries = new();

    /// <summary>
    /// 按 DriverId 精确匹配注册条目。若 driverId 为 null，返回第一个条目。重复 DriverId 注册时 first-wins，不抛异常但输出日志警告。
    /// </summary>
    public Entry? Find(string? driverId)
    {
        if (driverId != null)
            return Entries.FirstOrDefault(e => e.DriverId == driverId);
        return Entries.FirstOrDefault();
    }

    public sealed record Entry(
        /// <summary>驱动标识符。Add*Driver 扩展方法设置，用于多驱动区分。</summary>
        string DriverId,
        Type MachineType,
        Type StrategyType,
        Type HandlerType,
        Type TransportType,
        Type StrategyOptionsType,
        Type TransportOptionsType
    );
}
```

`Find(string? driverId)` 按 `DriverId` 精确匹配；若 `driverId` 为 null，返回第一个注册条目。

#### 3.3 各驱动的 Add 扩展方法

```csharp
// BatteryDriver 项目
public static IServiceCollection AddBatteryDriver(this IServiceCollection services)
{
    // 注册到全局注册表
    services.AddSingleton(_ => new DriverTypeRegistry.Entry(
        DriverId:             "Battery",   // 唯一标识符，用于多驱动场景下的查找
        MachineType:          typeof(Machine),
        StrategyType:         typeof(BatteryTcpStrategy),
        HandlerType:          typeof(TransportHandler),
        TransportType:        typeof(InfluxDbTransport),
        StrategyOptionsType:  typeof(BatteryTcpStrategyOptions),
        TransportOptionsType: typeof(InfluxDbTransportOptions)
    ));
    return services;
}

// 宿主注册时组合
builder.Services.AddBatteryDriver();      // 注册 BatteryTcpStrategy 元数据
builder.Services.AddInfluxDbTransport();  // 注册 InfluxDbTransport 元数据（如果与 Battery 不同）
```

**查找优先级**：`MachineScope` 构造函数通过 `registry.Find(config.DriverId)` 按 `DriverId` 精确匹配；若 `DriverId` 为 null，fallback 到第一个注册条目。
对于 Transport，若单独注册了 `AddInfluxDbTransport()`，它的 Entry 通过独立的列表管理或合并在同一 Entry 中。
简化处理：每个 `Add*Driver()` 扩展方法注册一个完整 Entry（含 Transport 类型）；
若宿主需要自定义 Transport，通过 `Add*Driver().WithTransport<T>()` 或直接构造 Entry 注册。

#### 3.4 宿主端用法

```csharp
var builder = Host.CreateApplicationBuilder();

// 必须：注册 ILoggerFactory（DI 容器需要 ILogger<T> 开放泛型）
builder.Services.AddLogging();

// 一行注册所有驱动基础设施
builder.Services.AddCollectionDrivers(
    builder.Configuration.GetSection("CollectionDrivers"));

// 显式注册需要的驱动
builder.Services.AddBatteryDriver();
builder.Services.AddFinsDriver();
builder.Services.AddOpcUaDriver();
```

`AddCollectionDrivers(IConfiguration section)` 内部：
1. `services.Configure<CollectionDriverOptions>(section)`
2. `services.AddSingleton<IMachineScopeFactory, MachineScopeFactory>()`
3. `services.AddHostedService<DriverHostService>()`

#### 3.5 NuGet 依赖

`CollectionDrivers.Common.csproj` 需新增：
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="8.0.0" />
```
（`Configuration.Binder` 提供 `IConfiguration.Bind(object)` 扩展方法）

---

### 第 4 节：Machine 生命周期 — MachineScope

#### 4.1 `IMachineScope` 接口

```csharp
/// <summary>单台机器的采集 Scope。封装 IServiceScope + 组件生命周期。</summary>
public interface IMachineScope : IAsyncDisposable
{
    /// <summary>机器上下文</summary>
    IMachineContext Context { get; }

    /// <summary>
    /// 启动采集循环：CreateAsync → InitializeAsync → SweepAsync 循环。
    /// 循环在 ct 取消或 Context.Enabled 变为 false 时退出。
    /// </summary>
    Task RunAsync(CancellationToken ct);
}
```

#### 4.2 整体流程

```
IHost
  └── DriverHostService (注入 IConfiguration + IOptions<CollectionDriverOptions> + IMachineScopeFactory)
        │
        ExecuteAsync:
        ├── 读 _options.Value.Machines
        ├── 遍历并填充每个 MachineConfig.Configuration（用 IConfiguration.GetSection 按索引）
        └── foreach enabled machineCfg:
              Task.Run(async () => {
                  await using var scope = _scopeFactory.CreateScope(machineCfg);
                  await scope.RunAsync(ct);
              })  ← 独立 Task，单机故障不传播
```

#### 4.3 `MachineScope` 实现

```csharp
internal class MachineScope : IMachineScope
{
    private readonly IServiceScope _scope;
    private readonly Strategy _strategy;
    private readonly Handler _handler;

    public IMachineContext Context { get; }

    internal MachineScope(
        IServiceScope scope,
        MachineOptions config,
        DriverTypeRegistry registry)
    {
        _scope = scope;
        var sp = scope.ServiceProvider;

        // ── Step 1: 查找类型注册 ──
        var entry = registry.Find(config.DriverId)
                    ?? throw new InvalidOperationException(
                        $"No driver registered for DriverId '{config.DriverId ?? "(null)"}'");

        // ── Step 2: 绑定 Options（从 config.Configuration 子段） ──
        var strategyOptions = BindOptions(
            config.Configuration?.GetSection("Strategy"), entry.StrategyOptionsType);
        var transportOptions = BindOptions(
            config.Configuration?.GetSection("Transport"), entry.TransportOptionsType);

        // ── Step 3: 构造 Machine ──
        var machineLogger = sp.GetRequiredService(typeof(ILogger<>).MakeGenericType(entry.MachineType));
        var machine = (Machine)ActivatorUtilities.CreateInstance(sp, entry.MachineType, machineLogger);
        machine.Initialize(config);
        Context = machine;

        // ── Step 4: 构造 Strategy ──
        var strategyLogger = sp.GetRequiredService(typeof(ILogger<>).MakeGenericType(entry.StrategyType));
        _strategy = (Strategy)ActivatorUtilities.CreateInstance(sp,
            entry.StrategyType, strategyLogger, Context, strategyOptions);
        _strategy.OnError += (ex, ctx) =>
            ((ILogger)strategyLogger).LogError(ex, "[{Id}] Strategy error in {Context}", Context.Id, ctx);

        // ── Step 5: 构造 Handler ──
        var handlerLogger = sp.GetRequiredService(typeof(ILogger<>).MakeGenericType(entry.HandlerType));
        _handler = (Handler)ActivatorUtilities.CreateInstance(sp,
            entry.HandlerType, handlerLogger, Context);

        // ── Step 6: 构造 Transport（暂单 Transport，后续可扩展为列表） ──
        var transportLogger = sp.GetRequiredService(typeof(ILogger<>).MakeGenericType(entry.TransportType));
        var transport = (Transport)ActivatorUtilities.CreateInstance(sp,
            entry.TransportType, transportLogger, Context, transportOptions);
        var transports = new List<Transport> { transport };

        // ── Step 7: 回挂到 Machine ──
        machine.SetStrategy(_strategy);
        machine.SetHandler(_handler);
        machine.SetTransports(transports);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // 生命周期：CreateAsync → InitializeAsync → SweepAsync 循环
        // CreateAsync 执行各组件的一次性初始化（打开连接、编译模板等）
        await _strategy.CreateAsync();
        foreach (var t in Context.Transports)
            await t.CreateAsync();
        await _handler.CreateAsync();

        await _strategy.InitializeAsync();
        while (!ct.IsCancellationRequested && Context.Enabled)
        {
            await _strategy.SweepAsync();
        }
        await Context.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        // 释放顺序：Transport → Strategy → Handler → Scope
        // 注意：RunAsync 循环退出时已调用过 Context.Stop()，此处不重复调用
        // （Stop() 语义上应为幂等，但避免未来非幂等重写导致双重副作用）
        foreach (var t in Context.Transports)
        {
            if (t is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (t is IDisposable d) d.Dispose();
        }
        if (_strategy is IAsyncDisposable sad) await sad.DisposeAsync();
        else if (_strategy is IDisposable sd) sd.Dispose();
        if (_handler is IAsyncDisposable had) await had.DisposeAsync();
        else if (_handler is IDisposable hd) hd.Dispose();
        _scope.Dispose();
    }

    /// <summary>
    /// 从 IConfiguration 段绑定驱动专用 Options。
    /// 若 section 为 null 或无内容，返回 Options 类型的默认实例（保持 DTO 中的默认值）。
    /// </summary>
    private static object BindOptions(IConfiguration? section, Type optionsType)
    {
        var options = Activator.CreateInstance(optionsType)!;
        if (section != null)
        {
            section.Bind(options);
        }
        return options;
    }
}
```

#### 4.4 关键设计说明

**配置绑定**：`BindOptions()` 先创建 Options 默认实例（带 DTO 定义的默认值），再用 `IConfigurationSection.Bind()` 覆盖。
`section` 为 null 时返回纯默认值。绑定失败（如类型转换错误）由 `IConfiguration.Bind()` 抛出 `InvalidOperationException`，
在 `MachineScope` 构造时即失败快速暴露。

**多 Transport**：当前 MachineScope 创建单个 Transport 实例。`IMachineContext.Transports` 保留为 `IReadOnlyList<Transport>`
以保持与 `TransportHandler`（遍历列表）的兼容性。未来如需支持多 Transport（如同发 InfluxDB + MQTT），
只需修改 `MachineScope` 构造函数中的 Transport 创建循环。

**故障隔离**：`DriverHostService.ExecuteAsync` 中每个 scope 包裹在独立的 `Task.Run` + `try-catch` 内，
单台机器崩溃仅记录日志，不影响其他机器或宿主关闭。

**多台同类型机器**：每台机器的 `Options` 通过独立 `IConfiguration` 段（`Machines:0:Strategy` vs `Machines:1:Strategy`）
绑定出独立的 Options 实例。三台 `BatteryTcpStrategy` 各自持有不同的端口配置。

---

### 第 5 节：DriverHostService 重构

```csharp
public class DriverHostService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IOptions<CollectionDriverOptions> _options;
    private readonly IMachineScopeFactory _scopeFactory;
    private readonly ILogger<DriverHostService> _logger;

    public DriverHostService(
        IConfiguration config,
        IOptions<CollectionDriverOptions> options,
        IMachineScopeFactory scopeFactory,
        ILogger<DriverHostService> logger)
    {
        _config = config;
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var machines = _options.Value.Machines.Where(m => m.Enabled).ToList();
        _logger.LogInformation("Starting {Count} machine(s)", machines.Count);

        var rootSection = _config.GetSection("CollectionDrivers");
        var tasks = new List<Task>();

        for (int i = 0; i < _options.Value.Machines.Count; i++)
        {
            var machineCfg = _options.Value.Machines[i];
            if (!machineCfg.Enabled) continue;

            // 🔗 填充 IConfiguration 引用——这是连接 IOptions 和 IConfiguration 的关键桥接
            machineCfg.Configuration = rootSection.GetSection($"Machines:{i}");

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await using var scope = _scopeFactory.CreateScope(machineCfg);
                    await scope.RunAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MachineId}] Machine crashed", machineCfg.Id);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }
}
```

**关键变化**：

| 现状 | 重构后 |
|------|--------|
| `LoggingFactory.SetProvider(loggerFactory)` | 删除 |
| `File.ReadAllTextAsync(yaml)` + YamlDotNet | 删除 |
| `Machines.CreateMachines(config)` | `_scopeFactory.CreateScope(machineCfg)` |
| 无隔离的 `RunMachineAsync` | 独立 `Task.Run` + try-catch |

---

### 第 6 节：迁移路径 — 增量共存

#### Phase 1：配置 DTO + 类型注册基础设施（非破坏性）

- 新增 `CollectionDriverOptions`、`MachineOptions`、各驱动 Options 类
- 新增 `DriverTypeRegistry`、`IMachineScopeFactory` 接口（实现留空或抛 `NotImplementedException`）
- 新增 `AddCollectionDrivers()`、`AddBatteryDriver()` 等扩展方法
- 新增 `IHandler`、`IMachineContext` 接口（暂不挂接）
- 新增 `Microsoft.Extensions.DependencyInjection.Abstractions` NuGet 引用
- 旧 YAML `dynamic` 路径不删
- **测试**：Option 类默认值测试、`IConfiguration.Bind()` 覆盖测试
- 验收：编译通过，旧逻辑不受影响

#### Phase 2：构造函数 `ILogger` 可选参数（向后兼容）

- 基类新增 `(ILogger? logger, ...)` 构造函数
- 子类新增 `(ILogger?, Machine, TOptions)` 构造函数（Phase 2 使用具体 Machine 类型）
- **同时保留旧构造函数**（标记 `[Obsolete]`），Activator.CreateInstance 继续使用旧签名
- `ILogger?` 为 null 时 fallback 到 `LoggingFactory`
- `[Obsolete]` 旧构造函数中 `readonly` Options 字段用 `= null!` 抑制 CS8618 警告（旧路径不使用 Options）
- **测试**：`new BatteryTcpStrategy(machine)` 仍可编译；
  新增 `new BatteryTcpStrategy(nullLogger, mockContext, options)` 测试
- 验收：新旧构造函数均可工作

#### Phase 3：Machine 接口化 + MachineOptions 替代 dynamic（破坏性）

- 移除 `Machine` 的 `abstract` 修饰符，使其可被 `ActivatorUtilities` 实例化
- `Machine` 实现 `IMachineContext`（含所有成员：`StrategySuccess`、`StrategyHealthy`、`Stop()` 等）
- `Machine.Initialize(MachineOptions)` 新方法替代 `dynamic` 配置
- `Strategy`/`Handler`/`Transport` 内部引用从 `Machine`（具体类）改为 `IMachineContext`
- 子类 `Machine.Configuration.*` 访问替换为注入的 Options 属性
- 新增 `Machine.SetStrategy/SetHandler/SetTransports` internal 方法
- `LoggingFactory` 标记 `[Obsolete]`
- **测试**：`new BatteryTcpStrategy(null, mockContext, options)` 模式
- 验收：编译通过，所有现有测试通过

#### Phase 4：DI 容器接管创建（破坏性）

- 实现 `MachineScopeFactory` + `MachineScope`
- `DriverHostService` 重构为薄编排层
- `Machine.AddStrategyAsync` 等反射方法标记 `[Obsolete]`
- 旧 `Machines.CreateMachines` 路径保留但新增模式优先（通过开关或代码路径并行）
- **测试**：MachineScope 集成测试（用真实 DI 容器构建）
- 验收：启动后采集逻辑正常运行

#### Phase 5：清理（清理性）

- 删除 `LoggingFactory`
- 删除 `Machine.AddStrategyAsync` / `AddHandlerAsync` / `AddTransportAsync` 反射方法
- 删除 `Machines` 类
- 删除空的 `BatteryMachine` / `FinsMachine` / `OpcUaMachine` / `ScannerMachine` 子类
- 删除 `dynamic` 配置路径和旧 `(Machine)` 构造函数
- 移除 `YamlDotNet` NuGet 引用（Common 项目不再需要）
- 验收：代码净减少

---

### 第 7 节：测试策略

#### 迁移期（Phase 1-4）

每个 Phase 的 PR 保持所有现有测试通过。

#### 最终状态（Phase 5）

**单元测试（Mock 依赖）**：

```csharp
[Fact]
public async Task SweepAsync_Disconnected_ReportsLastSuccessFalse()
{
    var logger = new NullLogger<BatteryTcpStrategy>();
    var context = new Mock<IMachineContext>();
    var options = new BatteryTcpStrategyOptions { Port = 13000 };

    context.Setup(c => c.Handler).Returns(Mock.Of<IHandler>());
    context.Setup(c => c.StrategySuccess).Returns(false);

    var strategy = new BatteryTcpStrategy(logger, context.Object, options);
    // TcpConnection 通过内部工厂注入 Mock

    await strategy.SweepAsync();

    Assert.False(strategy.LastSuccess);
    Assert.False(strategy.IsHealthy);
}
```

**集成测试（真实 DI 容器）**：

```csharp
[Fact]
public async Task MachineScope_RunAsync_CompletesSweepCycle()
{
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CollectionDrivers:Machines:0:Id"] = "test",
            ["CollectionDrivers:Machines:0:Enabled"] = "true",
            ["CollectionDrivers:Machines:0:SweepMs"] = "100",
        }).Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddCollectionDrivers(config.GetSection("CollectionDrivers"));
    services.AddBatteryDriver();

    var sp = services.BuildServiceProvider();
    var factory = sp.GetRequiredService<IMachineScopeFactory>();

    var machineCfg = sp.GetRequiredService<IOptions<CollectionDriverOptions>>()
        .Value.Machines[0];
    machineCfg.Configuration = config.GetSection("CollectionDrivers:Machines:0");

    await using var scope = factory.CreateScope(machineCfg);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await scope.RunAsync(cts.Token);
}
```

---

## 文件影响范围

### 新增文件

| 文件 | 项目 | Phase |
|------|------|-------|
| `CollectionDriverOptions.cs` | Common | 1 |
| `MachineOptions.cs` | Common | 1 |
| `IHandler.cs` | Common | 1 |
| `IMachineContext.cs` | Common | 1 |
| `IMachineScope.cs` | Common | 1 |
| `IMachineScopeFactory.cs` | Common | 1 |
| `MachineScope.cs` | Common | 4 |
| `MachineScopeFactory.cs` | Common | 4 |
| `DriverTypeRegistry.cs` | Common | 1 |
| `ServiceCollectionExtensions.cs` | Common | 1 |
| `BatteryTcpStrategyOptions.cs` | BatteryDriver | 1 |
| `FinsStrategyOptions.cs` | FinsDriver | 1 |
| `OpcUaStrategyOptions.cs` | OpcUaDriver | 1 |
| `ScannerStrategyOptions.cs` | ScannerDriver | 1 |
| `InfluxDbTransportOptions.cs` | Transport.InfluxDB | 1 |
| `BatteryDriverServiceExtensions.cs` | BatteryDriver | 1 |
| `FinsDriverServiceExtensions.cs` | FinsDriver | 1 |
| `OpcUaDriverServiceExtensions.cs` | OpcUaDriver | 1 |
| `ScannerDriverServiceExtensions.cs` | ScannerDriver | 1 |
| `InfluxDbTransportServiceExtensions.cs` | Transport.InfluxDB | 1 |

### 修改文件

| 文件 | 项目 | Phase |
|------|------|-------|
| `Strategy.cs` | Common | 2, 3 |
| `Machine.cs` | Common | 2, 3, 4 |
| `Handler.cs` | Common | 2, 3 |
| `Transport.cs` | Common | 2, 3 |
| `DriverHostService.cs` | Common | 4 |
| `Machines.cs` | Common | 4 (Obsolete), 5 (删除) |
| `BatteryMachine.cs` | BatteryDriver | 2, 5 (删除) |
| `BatteryTcpStrategy.cs` | BatteryDriver | 2, 3 |
| `FinsMachine.cs` | FinsDriver | 2, 5 (删除) |
| `FinsStrategy.cs` | FinsDriver | 2, 3 |
| `OpcUaMachine.cs` | OpcUaDriver | 2, 5 (删除) |
| `OpcUaStrategy.cs` | OpcUaDriver | 2, 3 |
| `ScannerMachine.cs` | ScannerDriver | 2, 5 (删除) |
| `ScannerStrategy.cs` | ScannerDriver | 2, 3 |
| `InfluxDbTransport.cs` | Transport.InfluxDB | 2, 3 |
| `CollectionDrivers.Common.csproj` | Common | 1 |
| `*Test.cs` | 各测试项目 | 2, 3, 4 |

### 删除文件（Phase 5）

| 文件 | 项目 | 说明 |
|------|------|------|
| `LoggingFactory.cs` | Common | DI 原生替代 |
| `Machines.cs` | Common | `MachineScopeFactory` 替代 |
| `BatteryMachine.cs` | BatteryDriver | Machine 基类替代 |
| `FinsMachine.cs` | FinsDriver | Machine 基类替代 |
| `OpcUaMachine.cs` | OpcUaDriver | Machine 基类替代 |
| `ScannerMachine.cs` | ScannerDriver | Machine 基类替代 |

---

## 关键设计决策

1. **`IHandler` 是必须的最小接口**：`IMachineContext` 暴露 Handler，循环依赖切断的前提
2. **不使用 Keyed Services**：.NET DI 不支持 Scope 级动态 key 注册。改用 `ActivatorUtilities` + 类型注册表
3. **`ActivatorUtilities` 而非纯 DI 解析**：DI 负责基础设施（ILogger），工厂控制组件组装
4. **Machine 子类消亡**：现有子类为空壳，直接使用 `Machine` + Options 配置
5. **向后兼容通过可选参数 + `[Obsolete]`**：每 Phase 编译可过、测试可过
6. **配置 DTO 不使用全局 `IOptions<T>`**：同类型多机器需要不同配置实例，MachineScope 按索引手动绑定
7. **故障隔离：独立 `Task.Run` + `try-catch`**：沿用原模式，单机故障不影响其他机器
8. **每机器独立 `IConfiguration` 段**：`Machines:{i}:Strategy` 自然隔离同类型机器的不同配置
9. **配置填充由 `DriverHostService` 完成**：持有 `IConfiguration` 和 `IOptions<CollectionDriverOptions>`，在 `ExecuteAsync` 中按索引填充 `MachineOptions.Configuration`
10. **`SweepAsync` 暂不添加 `CancellationToken` 参数**：当前 `Task.Delay(SweepMs)` 不传 token，关闭延迟最多 SweepMs。DI 重构是添加的好时机，但属独立优化，不在本设计范围——如需添加可在后续 PR 独立进行
