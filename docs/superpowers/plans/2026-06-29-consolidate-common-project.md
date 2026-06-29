# 合并 base-driver 到 CollectionDrivers.Common 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除 base-driver 项目，将 6 个核心类迁入 CollectionDrivers.Common，清理所有 Veneer/fanuc 遗留代码，统一命名空间为 CollectionDrivers.Common。

**Architecture:** 复制 base-driver/base/ 的 6 个文件到 Common 并精简 namespace 和代码，删除 3 个废弃文件，更新所有跨项目引用（using/csproj/slnx），同步清理 InfluxDbTransport 的 Veneer 依赖。

**Tech Stack:** .NET 8, C#, Newtonsoft.Json, YamlDotNet

**TDD 跳过声明:** 纯机械重构——文件迁移、命名空间替换、删除死代码。不改变任何业务逻辑，不引入新功能。所有步骤编译+测试两轮验证保证安全。

---

### Task 1: 迁移 6 个类到 CollectionDrivers.Common

**Files:**
- Create: `src/CollectionDrivers.Common/Machine.cs`
- Create: `src/CollectionDrivers.Common/Handler.cs`
- Create: `src/CollectionDrivers.Common/Transport.cs`
- Create: `src/CollectionDrivers.Common/Strategy.cs`
- Create: `src/CollectionDrivers.Common/Machines.cs`
- Create: `src/CollectionDrivers.Common/LoggingFactory.cs`

- [ ] **Step 1: 写入精简后的 Machine.cs**

```csharp
// ReSharper disable once CheckNamespace

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

public abstract class Machine
{
    protected readonly ILogger Logger;
    private Machines _machines;

    protected Machine(Machines machines, object configuration)
    {
        Configuration = configuration;
        Enabled = Configuration.machine.enabled;
        Logger = LoggingFactory.CreateLogger(typeof(Machine).FullName);
        Logger.LogDebug($"[{Id}] Creating machine, enabled: {Enabled}");
        _machines = machines;
        _propertyBag = new Dictionary<string, dynamic>();
    }

    public dynamic Configuration { get; }

    public virtual dynamic Info => new {_id = Id};
    public bool Enabled { get; private set; }

    public string Id => Configuration.machine.id;

    public override string ToString()
    {
        return new {Id}.ToString()!;
    }

    public void Disable()
    {
        Enabled = false;
    }

    public virtual async Task Stop()
    {
        
    }
    
    #region property-bag

    private readonly Dictionary<string, dynamic> _propertyBag;

    public dynamic? this[string propertyBagKey]
    {
        get
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
                return _propertyBag[propertyBagKey];
            return null;
        }

        // ReSharper disable once PropertyCanBeMadeInitOnly.Global
        // ReSharper disable once MemberCanBeProtected.Global
        set
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
            {
                _propertyBag[propertyBagKey] = value!;
            }
            else
            {
                Logger.LogDebug($"[{Id}] Adding '{propertyBagKey}' to property bag.");
                _propertyBag.Add(propertyBagKey, value);
            }
        }
    }

    #endregion

    #region handler

    public Handler Handler { get; private set; } = null!;

    public async Task<Machine> AddHandlerAsync(Type type)
    {
        Logger.LogDebug($"[{Id}] Creating handler: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Handler = (Handler) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Handler!.CreateAsync();
        }
        catch
        {
            Logger.LogError($"[{Id}] Unable to add handler: {type.FullName}");
        }

        return this;
    }

    #endregion

    #region strategy

    public bool StrategySuccess => Strategy.LastSuccess;
    public bool StrategyHealthy => Strategy.IsHealthy;
    public Strategy Strategy { get; private set; } = null!;

    public async Task<Machine> AddStrategyAsync(Type type)
    {
        Logger.LogDebug($"[{Id}] Creating strategy: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Strategy = (Strategy) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Strategy!.CreateAsync();
        }
        catch
        {
            Logger.LogError($"[{Id}] Unable to add strategy: {type.FullName}");
        }

        return this;
    }

    public async Task InitStrategyAsync()
    {
        Logger.LogDebug($"[{Id}] Initializing strategy...");
        await Strategy.InitializeAsync();
    }

    public async Task RunStrategyAsync()
    {
        await Strategy.SweepAsync();
    }

    #endregion

    #region transport

    public Transport Transport { get; private set; } = null!;

    public async Task<Machine> AddTransportAsync(Type type)
    {
        Logger.LogDebug($"[{Id}] Creating transport: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Transport = (Transport) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Transport!.CreateAsync();
        }
        catch
        {
            Logger.LogError($"[{Id}] Unable to add transport: {type.FullName}");
        }

        return this;
    }

    #endregion
}
```

- [ ] **Step 2: 写入精简后的 Handler.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Handler
{
    protected readonly ILogger Logger;

    protected Handler(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    public Machine Machine { get; }

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task OnStrategySweepCompleteInternalAsync()
    {
        var beforeRet = await BeforeSweepCompleteAsync(Machine);
        var onRet = await OnStrategySweepCompleteAsync(Machine, beforeRet);
        await AfterSweepCompleteAsync(Machine, onRet);
    }

    protected virtual async Task<dynamic?> BeforeSweepCompleteAsync(Machine machine)
    {
        return null;
    }

    protected virtual async Task<dynamic?> OnStrategySweepCompleteAsync(Machine machine, dynamic? beforeSweepComplete)
    {
        return null;
    }

    protected virtual async Task AfterSweepCompleteAsync(Machine machine, dynamic? onSweepComplete)
    {
    }
}
```

- [ ] **Step 3: 写入精简后的 Transport.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Transport
{
    protected readonly ILogger Logger;

    // ReSharper disable once UnusedParameter.Local
    protected Transport(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    protected Machine Machine { get; }

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task ConnectAsync()
    {
    }

    public virtual async Task SendAsync(params dynamic[] parameters)
    {
    }
}
#pragma warning restore CS1998
```

- [ ] **Step 4: 写入 Strategy.cs（仅改 namespace）**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Strategy
{
    protected readonly ILogger Logger;
    protected readonly int SweepMs;

    protected Strategy(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
        SweepMs = machine.Configuration.type["sweep_ms"];
    }

    public Machine Machine { get; }
    public bool LastSuccess { get; protected set; }
    public bool IsHealthy { get; protected set; }

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task<dynamic?> InitializeAsync()
    {
        return null;
    }

    protected virtual async Task<dynamic?> CollectAsync()
    {
        return null;
    }

    public virtual async Task SweepAsync(int delayMs = -1)
    {
        delayMs = delayMs < 0 ? SweepMs : delayMs;
        await Task.Delay(delayMs);
        LastSuccess = false;
        await CollectAsync();
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
```

- [ ] **Step 5: 写入精简后的 Machines.cs**

```csharp
// ReSharper disable once CheckNamespace

using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

public class Machines
{
    private readonly ILogger _logger;
    private readonly List<Machine> _machines;
    private readonly Dictionary<string, dynamic> _propertyBag;

    private Machines()
    {
        _logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);
        _machines = new List<Machine>();
        _propertyBag = new Dictionary<string, dynamic>();
    }

    public dynamic? this[string propertyBagKey]
    {
        get
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
                return _propertyBag[propertyBagKey];
            return null;
        }

        set
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
            {
#pragma warning disable CS8601
                _propertyBag[propertyBagKey] = value;
#pragma warning restore CS8601
            }
            else
            {
                _propertyBag.Add(propertyBagKey, value);
            }
        }
    }

    private Machine? Add(dynamic configuration)
    {
        // 防止创建已禁用的机器
        if (configuration.machine.enabled == false)
        {
            _logger.LogInformation($"[{configuration.machine.id}] Machine disabled and will not be added");
            return null;
        }
        
        _logger.LogDebug($"Adding machine:\n{JObject.FromObject(configuration.machine).ToString()}");

        try
        {
            Type machineType = Type.GetType(configuration.machine.type);
            Machine machine = (Machine) Activator.CreateInstance(machineType, new object[] {this, configuration})!;
            _machines.Add(machine);
            return machine;
        }
        catch (Exception e)
        {
            _logger.LogError($"[{configuration.machine.id}] Failed to add machine");
            return null;
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();

        foreach (var machine in _machines.Where(x => x.Enabled))
        {
            tasks.Add(Task.Run(async () =>
            {
                await RunMachineAsync(machine, stoppingToken);
            }, stoppingToken));
        }

        _logger.LogInformation("Machine tasks running");

        await Task.WhenAll(tasks);
        
        _logger.LogInformation("Machine tasks stopped");
    }

    private async Task RunMachineAsync(Machine machine, CancellationToken stoppingToken)
    {
        await machine.InitStrategyAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await machine.RunStrategyAsync();
        }

        _logger.LogInformation($"[{machine.Id}] Machine task stopping");
        
        await machine.Stop();
        
        _logger.LogInformation($"[{machine.Id}] Machine task stopped");
    }

    public static async Task<Machines> CreateMachines(dynamic config)
    {
        var logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);

        var machineConfigs = new List<dynamic>();

        foreach (var machineConf in config["machines"])
        {
            var prebuiltConfig = new
            {
                machine = new
                {
                    id = machineConf.ContainsKey("id") ? machineConf["id"] : Guid.NewGuid().ToString(),
                    enabled = machineConf.ContainsKey("enabled") ? machineConf["enabled"] : false,
                    type = machineConf.ContainsKey("type")
                        ? machineConf["type"]
                        : null,
                    strategy = machineConf.ContainsKey("strategy")
                        ? machineConf["strategy"]
                        : null,
                    handler = machineConf.ContainsKey("handler")
                        ? machineConf["handler"]
                        : null,
                    transport = machineConf.ContainsKey("transport")
                        ? machineConf["transport"]
                        : null
                }
            };

            var builtConfig = new
            {
                prebuiltConfig.machine,
                type = prebuiltConfig.machine.type != null && machineConf.ContainsKey(prebuiltConfig.machine.type)
                    ? machineConf[prebuiltConfig.machine.type]
                    : new Dictionary<object, object>(),
                strategy = prebuiltConfig.machine.strategy != null && machineConf.ContainsKey(prebuiltConfig.machine.strategy)
                    ? machineConf[prebuiltConfig.machine.strategy]
                    : new Dictionary<object, object>(),
                handler = prebuiltConfig.machine.handler != null && machineConf.ContainsKey(prebuiltConfig.machine.handler)
                    ? machineConf[prebuiltConfig.machine.handler]
                    : new Dictionary<object, object>(),
                transport = prebuiltConfig.machine.transport != null && machineConf.ContainsKey(prebuiltConfig.machine.transport)
                    ? machineConf[prebuiltConfig.machine.transport]
                    : new Dictionary<object, object>()
            };

            // ReSharper disable once RedundantToStringCall
            logger.LogTrace($"Machine configuration built:\n{JObject.FromObject(builtConfig)}");

            machineConfigs.Add(builtConfig);
        }

        var machines = new Machines();

        foreach (var cfg in machineConfigs)
        {
            logger.LogTrace($"Creating machine from config:\n{JObject.FromObject(cfg)}");

            Machine machine = machines.Add(cfg);

            if (machine != null)
            {
                try
                {
                    Type transportType = Type.GetType(cfg.machine.transport);
                    Type strategyType = Type.GetType(cfg.machine.strategy);
                    Type handlerType = Type.GetType(cfg.machine.handler);
                
                    await machine.AddTransportAsync(transportType);
                    await machine.AddStrategyAsync(strategyType);
                    await machine.AddHandlerAsync(handlerType);
                }
                catch (Exception e)
                {
                    logger.LogError($"[{machine.Id}] Failed to create machine");
                    machine.Disable();
                }
            }
        }

        return machines;
    }
}
```

- [ ] **Step 6: 写入 LoggingFactory.cs（仅改 namespace）**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

/// <summary>
/// 静态日志工厂，封装 ILoggerFactory 的生命周期。
/// 未配置时默认使用 NullLogger（无日志输出）。
/// 宿主在启动时调用 SetProvider() 配置具体日志提供程序。
/// </summary>
public static class LoggingFactory
{
    private static ILoggerFactory _factory = NullLoggerFactory.Instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 设置日志提供程序。由宿主在应用程序启动时调用。
    /// 未调用时默认使用 NullLogger（无日志输出）。
    /// </summary>
    /// <param name="factory">日志工厂实例。不允许为 null。</param>
    /// <exception cref="ArgumentNullException">factory 为 null 时抛出</exception>
    public static void SetProvider(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_lock)
        {
            var old = _factory;
            _factory = factory;
            (old as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 获取或创建指定类别名称的日志记录器。
    /// 使用 Volatile.Read 确保多线程下的引用可见性。
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
        => Volatile.Read(ref _factory).CreateLogger(categoryName);

    /// <summary>
    /// 释放底层日志工厂，并将工厂重置为 NullLogger。
    /// 宿主应在应用程序关闭时调用。重置后日志静默丢弃。
    /// </summary>
    public static void Close()
    {
        lock (_lock)
        {
            (_factory as IDisposable)?.Dispose();
            _factory = NullLoggerFactory.Instance;
        }
    }
}
```

- [ ] **Step 7: 提交 Task 1**

```bash
git add src/CollectionDrivers.Common/Machine.cs src/CollectionDrivers.Common/Handler.cs src/CollectionDrivers.Common/Transport.cs src/CollectionDrivers.Common/Strategy.cs src/CollectionDrivers.Common/Machines.cs src/CollectionDrivers.Common/LoggingFactory.cs
git commit -m "feat: 迁移 base-driver 核心类到 CollectionDrivers.Common 命名空间"
```

---

### Task 2: 更新 Common.csproj（引用 + 依赖）

**Files:**
- Modify: `src/CollectionDrivers.Common/CollectionDrivers.Common.csproj`

- [ ] **Step 1: 修改 csproj**

将完整内容替换为：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 提交**

```bash
git add src/CollectionDrivers.Common/CollectionDrivers.Common.csproj
git commit -m "feat: Common.csproj 移除 base-driver 引用，新增自身所需依赖"
```

---

### Task 3: 清理 InfluxDbTransport.cs（移除 Veneer 依赖）

**Files:**
- Modify: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs`

- [ ] **Step 1: 写入精简后的 InfluxDbTransport.cs**

```csharp
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Microsoft.Extensions.Logging;
using Scriban;

namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB 传输层。将采集数据通过 Line Protocol 写入 InfluxDB。
/// 支持通过 Scriban 模板进行数据变换。
/// </summary>
public class InfluxDbTransport : CollectionDrivers.Common.Transport
{
    /// <summary>
    /// 已编译的 Scriban 模板缓存：模板名称 → 模板。
    /// </summary>
    private readonly Dictionary<string, Template> _templateLookup = new();

    private InfluxDBClient _client = null!;
    private WriteApiAsync _writeApi = null!;

    /// <summary>
    /// 配置中的变换器映射：键 → Scriban 模板文本。
    /// </summary>
    private Dictionary<string, string> _transformLookup = new();

    /// <summary>
    /// 缓存配置项，避免重复从动态对象读取。
    /// </summary>
    private string _bucket = string.Empty;
    private string _org = string.Empty;

    public InfluxDbTransport(CollectionDrivers.Common.Machine machine) : base(machine)
    {
    }

    /// <summary>
    /// 初始化 InfluxDB 客户端，解析变换模板配置。
    /// </summary>
    public override async Task<dynamic?> CreateAsync()
    {
        var transportCfg = Machine.Configuration.transport;

        _bucket = transportCfg.ContainsKey("bucket") ? (string)transportCfg["bucket"] : "default";
        _org = transportCfg.ContainsKey("org") ? (string)transportCfg["org"] : "default";

        var host = transportCfg.ContainsKey("host") ? (string)transportCfg["host"] : "http://localhost:8086";
        var token = transportCfg.ContainsKey("token") ? (string)transportCfg["token"] : string.Empty;

        Logger.LogInformation("[{MachineId}] Creating InfluxDB client: {Host}, bucket={Bucket}, org={Org}",
            Machine.Id, host, _bucket, _org);

        _client = InfluxDBClientFactory.Create(host, token);
        _writeApi = _client.GetWriteApiAsync();

        if (transportCfg.ContainsKey("transformers") && transportCfg["transformers"] != null)
        {
            _transformLookup = (transportCfg["transformers"] as IDictionary<object, object>)
                ?.ToDictionary(kv => (string)kv.Key, kv => (string)kv.Value)
                ?? new Dictionary<string, string>();

            Logger.LogInformation("[{MachineId}] Loaded {Count} transformer(s)", Machine.Id, _transformLookup.Count);
        }

        return null;
    }

    /// <summary>
    /// InfluxDB 客户端在内部管理连接，此处无需额外操作。
    /// </summary>
    public override async Task ConnectAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 根据事件类型将数据写入 InfluxDB。
    /// 当前仅处理 SWEEP_END 事件。
    /// </summary>
    /// <param name="parameters">[0]=事件名, [1]=保留(null), [2]=payload</param>
    public override async Task SendAsync(params dynamic[] parameters)
    {
        var @event = (string)parameters[0];
        var data = parameters[2];

        switch (@event)
        {
            case "SWEEP_END":
                await HandleSweepEndAsync(data);
                break;

            case "INT_MODEL":
                // 中间模型暂不处理
                break;
        }
    }

    /// <summary>
    /// 处理采集周期结束事件：若有 "SWEEP_END" 变换模板则渲染并写入。
    /// </summary>
    private async Task HandleSweepEndAsync(dynamic data)
    {
        if (!HasTransform("SWEEP_END")) return;

        var template = _templateLookup["SWEEP_END"];
        var lp = template.Render(new { data.observation, data.state.data });

        if (string.IsNullOrEmpty(lp)) return;

        Logger.LogDebug("[{MachineId}] Writing SWEEP_END: {LineProtocol}", Machine.Id, lp);
        await _writeApi.WriteRecordAsync(lp, WritePrecision.Ms, _bucket, _org);
    }

    /// <summary>
    /// 检查并缓存指定名称的变换模板。
    /// </summary>
    private bool HasTransform(string templateName)
    {
        // 已缓存
        if (_templateLookup.ContainsKey(templateName))
            return true;

        // 配置中存在，编译并缓存
        if (_transformLookup.TryGetValue(templateName, out var transformText))
        {
            var template = Template.Parse(transformText);
            if (template.HasErrors)
            {
                Logger.LogError("[{MachineId}] '{TemplateName}' template transform has errors: {Errors}",
                    Machine.Id, templateName,
                    string.Join("; ", template.Messages));
                return false;
            }

            _templateLookup[templateName] = template;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 释放 InfluxDB 客户端资源。
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
    }
}
```

- [ ] **Step 2: 提交**

```bash
git add src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs
git commit -m "feat: 清理 InfluxDbTransport 的 Veneer 依赖，简化为仅处理 SWEEP_END"
```

---

### Task 4: 更新所有 using 语句（12 src + 1 test）

**Files:**
- Modify: `src/CollectionDrivers.Common/DriverHostService.cs:1`
- Modify: `src/CollectionDrivers.Common/TransportHandler.cs:1`
- Modify: `src/CollectionDrivers.BatteryDriver/BatteryHandler.cs:1`
- Modify: `src/CollectionDrivers.BatteryDriver/BatteryMachine.cs:1`
- Modify: `src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs:3`
- Modify: `src/CollectionDrivers.FinsDriver/FinsMachine.cs:1`
- Modify: `src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs:2`
- Modify: `src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs:1`
- Modify: `src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs:2`
- Modify: `src/CollectionDrivers.ScannerDriver/ScannerMachine.cs:1`
- Modify: `src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs:3`
- Modify: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs`（已在 Task 3 处理，无需重复）
- Modify: `tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs:4`

- [ ] **Step 1: DriverHostService.cs 第 1 行**

```csharp
// 删除 using l99.driver.@base;
// 无替换（DriverHostService 已在 CollectionDrivers.Common 命名空间）
```

- [ ] **Step 2: TransportHandler.cs 第 1 行**

```csharp
// 删除 using l99.driver.@base;
// 无替换（TransportHandler 已在 CollectionDrivers.Common 命名空间）
```

- [ ] **Step 3: BatteryHandler.cs 第 1 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 4: BatteryMachine.cs 第 1 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 5: BatteryTcpStrategy.cs 第 3 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 6: FinsMachine.cs 第 1 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 7: FinsStrategy.cs 第 2 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 8: OpcUaMachine.cs 第 1 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 9: OpcUaStrategy.cs 第 2 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 10: ScannerMachine.cs 第 1 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 11: ScannerStrategy.cs 第 3 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 12: BatteryTcpStrategyTest.cs 第 4 行**

```csharp
// 将
using l99.driver.@base;
// 改为
using CollectionDrivers.Common;
```

- [ ] **Step 13: 提交**

```bash
git add src/CollectionDrivers.Common/DriverHostService.cs src/CollectionDrivers.Common/TransportHandler.cs src/CollectionDrivers.BatteryDriver/BatteryHandler.cs src/CollectionDrivers.BatteryDriver/BatteryMachine.cs src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs src/CollectionDrivers.FinsDriver/FinsMachine.cs src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs src/CollectionDrivers.ScannerDriver/ScannerMachine.cs src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs
git commit -m "feat: 全局 using 从 l99.driver.@base 迁移到 CollectionDrivers.Common"
```

---

### Task 5: 更新 csproj 引用 + slnx

**Files:**
- Modify: `src/CollectionDrivers.BatteryDriver/CollectionDrivers.BatteryDriver.csproj:4`
- Modify: `src/CollectionDrivers.FinsDriver/CollectionDrivers.FinsDriver.csproj:20`
- Modify: `src/CollectionDrivers.OpcUaDriver/CollectionDrivers.OpcUaDriver.csproj:4`
- Modify: `src/CollectionDrivers.ScannerDriver/CollectionDrivers.ScannerDriver.csproj:5`
- Modify: `src/CollectionDrivers.Transport.InfluxDB/CollectionDrivers.Transport.InfluxDB.csproj:11`
- Modify: `collection-drivers.slnx`

- [ ] **Step 1: BatteryDriver.csproj 第 4 行**

```xml
<!-- 将 -->
<ProjectReference Include="..\base-driver\base-driver.csproj" />
<!-- 改为 -->
<ProjectReference Include="..\CollectionDrivers.Common\CollectionDrivers.Common.csproj" />
```

- [ ] **Step 2: FinsDriver.csproj 第 20 行**

```xml
<!-- 将 -->
<ProjectReference Include="..\base-driver\base-driver.csproj" />
<!-- 改为 -->
<ProjectReference Include="..\CollectionDrivers.Common\CollectionDrivers.Common.csproj" />
```

- [ ] **Step 3: OpcUaDriver.csproj 第 4 行**

```xml
<!-- 将 -->
<ProjectReference Include="..\base-driver\base-driver.csproj" />
<!-- 改为 -->
<ProjectReference Include="..\CollectionDrivers.Common\CollectionDrivers.Common.csproj" />
```

- [ ] **Step 4: ScannerDriver.csproj 第 5 行**

```xml
<!-- 删除该行 -->
<ProjectReference Include="..\base-driver\base-driver.csproj" />
<!-- Common 引用已在第 4 行存在，无需新增 -->
```

- [ ] **Step 5: Transport.InfluxDB.csproj 第 11 行**

```xml
<!-- 将 -->
<ProjectReference Include="..\base-driver\base-driver.csproj" />
<!-- 改为 -->
<ProjectReference Include="..\CollectionDrivers.Common\CollectionDrivers.Common.csproj" />
```

- [ ] **Step 6: 更新 collection-drivers.slnx**

删除 base-driver 行，最终文件内容：

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/CollectionDrivers.BatteryDriver/CollectionDrivers.BatteryDriver.csproj" />
    <Project Path="src/CollectionDrivers.Common/CollectionDrivers.Common.csproj" />
    <Project Path="src/CollectionDrivers.FinsDriver/CollectionDrivers.FinsDriver.csproj" />
    <Project Path="src/CollectionDrivers.OpcUaDriver/CollectionDrivers.OpcUaDriver.csproj" />
    <Project Path="src/CollectionDrivers.ScannerDriver/CollectionDrivers.ScannerDriver.csproj" />
    <Project Path="src/CollectionDrivers.Transport.InfluxDB/CollectionDrivers.Transport.InfluxDB.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/battery-driver.test/battery-driver.test.csproj" />
    <Project Path="tests/fins-driver.test/fins-driver.test.csproj" />
    <Project Path="tests/opcua-driver.test/opcua-driver.test.csproj" />
    <Project Path="tests/scanner-driver.test/scanner-driver.test.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 7: 编译验证**

```bash
dotnet build
```

预期: Build succeeded, 0 Error(s)。可能出现 CS0618 警告（BatteryHandler 的 [Obsolete]），可忽略。

- [ ] **Step 8: 提交**

```bash
git add src/CollectionDrivers.BatteryDriver/CollectionDrivers.BatteryDriver.csproj src/CollectionDrivers.FinsDriver/CollectionDrivers.FinsDriver.csproj src/CollectionDrivers.OpcUaDriver/CollectionDrivers.OpcUaDriver.csproj src/CollectionDrivers.ScannerDriver/CollectionDrivers.ScannerDriver.csproj src/CollectionDrivers.Transport.InfluxDB/CollectionDrivers.Transport.InfluxDB.csproj collection-drivers.slnx
git commit -m "feat: 所有项目引用从 base-driver 切换到 CollectionDrivers.Common"
```

---

### Task 6: 删除 base-driver 目录 + 废弃文件

**Files:**
- Delete: `src/base-driver/`（整个目录）

- [ ] **Step 1: 删除 base-driver 目录**

```bash
rm -rf src/base-driver
```

- [ ] **Step 2: 运行全部测试**

```bash
dotnet test
```

预期: 42 passed, 0 failed

- [ ] **Step 3: 提交**

```bash
git add src/base-driver/
git commit -m "feat: 删除 base-driver 项目，全部代码已迁移至 CollectionDrivers.Common"
```
