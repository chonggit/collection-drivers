# base-driver 日志框架替换实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 base-driver 的日志框架从 NLog 直接调用替换为 Microsoft.Extensions.Logging.Abstractions，彻底移除 NLog 依赖，未配置时默认为 NullLogger

**Architecture:** 通过新增静态 LoggingFactory 类封装 ILoggerFactory，各基类用 LoggingFactory.CreateLogger() 替换 LogManager.GetLogger()，Bootstrap.cs 移除 NLog 初始化逻辑，日志提供程序由宿主决定

**Tech Stack:** .NET 8, Microsoft.Extensions.Logging.Abstractions 8.0.0

**TDD 跳过声明**：纯机械性替换工作（NLog.ILogger → MEL ILogger，LogManager → LoggingFactory），无新业务逻辑，无 RED→GREEN 循环，跳过 TDD

---

### Task 1: 更新 NuGet 包依赖

**Files:**
- Modify: `src/base-driver/base-driver.csproj:11-16`
- Modify: `src/battery-driver/battery-driver.csproj:7-8`

- [ ] **Step 1: 更新 base-driver.csproj — 移除 NLog，新增 MEL Abstractions**

将 base-driver.csproj 的 PackageReference 段改为：

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
  </ItemGroup>
```

移除：`NLog` (line 14)、`NLog.Extensions.Logging` (line 15)
新增：`Microsoft.Extensions.Logging.Abstractions` 8.0.0

- [ ] **Step 2: 更新 battery-driver.csproj — 移除 NLog 死依赖**

```xml
<ItemGroup>
    <ProjectReference Include="..\base-driver\base-driver.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
  </ItemGroup>
```

移除 `<PackageReference Include="NLog" Version="6.1.3" />` (line 8)

- [ ] **Step 3: 提交 NuGet 依赖变更**

```bash
git add src/base-driver/base-driver.csproj src/battery-driver/battery-driver.csproj
git commit -m "chore: 替换 NLog 为 Microsoft.Extensions.Logging.Abstractions"
```

---

### Task 2: 创建 LoggingFactory.cs

**Files:**
- Create: `src/base-driver/base/LoggingFactory.cs`

- [ ] **Step 1: 编写 LoggingFactory 静态类**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

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

- [ ] **Step 2: 提交 LoggingFactory**

```bash
git add src/base-driver/base/LoggingFactory.cs
git commit -m "feat: 添加静态 LoggingFactory 类封装 ILoggerFactory"
```

---

### Task 3: 替换 Machine.cs 和 Machines.cs 的 Logger

**Files:**
- Modify: `src/base-driver/base/Machine.cs:1-17`
- Modify: `src/base-driver/base/Machines.cs:1-16,109`

- [ ] **Step 1: 替换 Machine.cs**

```csharp
// ReSharper disable once CheckNamespace

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace l99.driver.@base;

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
        Veneers = new Veneers(this);
        _propertyBag = new Dictionary<string, dynamic>();
    }
```

变更：
- `using NLog;` → `using Microsoft.Extensions.Logging;`
- `NLog.ILogger` → `Microsoft.Extensions.Logging.ILogger`（但 using 后只需 `ILogger`）
- `LogManager.GetCurrentClassLogger()` → `LoggingFactory.CreateLogger(typeof(Machine).FullName)`
- `Logger.Debug(` → `Logger.LogDebug(`

余下两个 Logger 调用：
- line 69: `Logger.Debug($"[{Id}] Adding '{propertyBagKey}' to property bag.");`
  → `Logger.LogDebug($"[{Id}] Adding '{propertyBagKey}' to property bag.");`
- line 83: `Logger.Debug($"[{Id}] Creating handler: {type.FullName}");`
  → `Logger.LogDebug($"[{Id}] Creating handler: {type.FullName}");`
- line 98: `Logger.Error($"[{Id}] Unable to add handler: {type.FullName}");`
  → `Logger.LogError($"[{Id}] Unable to add handler: {type.FullName}");`
- line 114: `Logger.Debug($"[{Id}] Creating strategy: {type.FullName}");`
  → `Logger.LogDebug($"[{Id}] Creating strategy: {type.FullName}");`
- line 126: `Logger.Error($"[{Id}] Unable to add strategy: {type.FullName}");`
  → `Logger.LogError($"[{Id}] Unable to add strategy: {type.FullName}");`
- line 134: `Logger.Debug($"[{Id}] Initializing strategy...");`
  → `Logger.LogDebug($"[{Id}] Initializing strategy...");`
- line 151: `Logger.Debug($"[{Id}] Creating transport: {type.FullName}");`
  → `Logger.LogDebug($"[{Id}] Creating transport: {type.FullName}");`
- line 163: `Logger.Error($"[{Id}] Unable to add transport: {type.FullName}");`
  → `Logger.LogError($"[{Id}] Unable to add transport: {type.FullName}");`
- line 179: `Logger.Debug($"[{Id}] Applying veneer: {type.FullName}");`
  → `Logger.LogDebug($"[{Id}] Applying veneer: {type.FullName}");`
- line 195: `Logger.Debug($"[{Id}] Applying veneer: {type.FullName}");`
  → `Logger.LogDebug($"[{Id}] Applying veneer: {type.FullName}");`

- [ ] **Step 2: 替换 Machines.cs（2 处）**

构造函数中：
```csharp
using Microsoft.Extensions.Logging;
// ...
private Machines()
{
    _logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);
    _machines = new List<Machine>();
    _propertyBag = new Dictionary<string, dynamic>();
}
```

`CreateMachines` 静态方法中（line 109）：
```csharp
var logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);
```

所有 `_logger.Info(` → `_logger.LogInformation(`
所有 `_logger.Debug(` → `_logger.LogDebug(`
所有 `_logger.Error(` → `_logger.LogError(`
所有 `_logger.Trace(` → `_logger.LogTrace(`

- [ ] **Step 3: 提交 Machine/Machines 变更**

```bash
git add src/base-driver/base/Machine.cs src/base-driver/base/Machines.cs
git commit -m "feat: 替换 Machine 和 Machines 中的 NLog Logger 为 MEL ILogger"
```

---

### Task 4: 替换 Transport、Strategy、Handler 的 Logger

**Files:**
- Modify: `src/base-driver/base/Transport.cs:1-15`
- Modify: `src/base-driver/base/Strategy.cs:1-15`
- Modify: `src/base-driver/base/Handler.cs:1-14`

这三个类使用相同模式 — `LogManager.GetLogger(GetType().FullName)` 获取运行时类型 Logger。

- [ ] **Step 1: 替换 Transport.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

public class Transport
{
    protected readonly ILogger Logger;

    // ReSharper disable once UnusedParameter.Local
    protected Transport(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }
```

将所有 `Logger.Debug(` → `Logger.LogDebug(`、`Logger.Trace(` → `Logger.LogTrace(` 等（Transport.cs 无显式日志调用，仅声明 Logger，所以只需要替换 using/field/初始化）。

- [ ] **Step 2: 替换 Strategy.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

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
```

Strategy.cs 无显式日志调用，仅声明 Logger。

- [ ] **Step 3: 替换 Handler.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

public class Handler
{
    protected readonly ILogger Logger;

    protected Handler(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }
```

Handler.cs 无显式日志调用，仅声明 Logger。

- [ ] **Step 4: 提交 Transport/Strategy/Handler 变更**

```bash
git add src/base-driver/base/Transport.cs src/base-driver/base/Strategy.cs src/base-driver/base/Handler.cs
git commit -m "feat: 替换 Transport、Strategy、Handler 中的 NLog Logger 为 MEL ILogger"
```

---

### Task 5: 替换 Veneer.cs 和 Veneers.cs 的 Logger

**Files:**
- Modify: `src/base-driver/base/Veneer.cs:1-47`
- Modify: `src/base-driver/base/Veneers.cs:1-22`

- [ ] **Step 1: 替换 Veneer.cs**

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

public class Veneer
{
    protected readonly ILogger Logger;
```

Logger 初始化（line 47）：
```csharp
Logger = LoggingFactory.CreateLogger(GetType().FullName);
```

日志调用变更：
- line 57: `Logger.Trace(` → `Logger.LogTrace(`
- line 67: `Logger.Trace(` → `Logger.LogTrace(`
- line 85: `Logger.Debug(` → `Logger.LogDebug(`
- line 89: `Logger.Info(` → `Logger.LogInformation(`

- [ ] **Step 2: 替换 Veneers.cs**

```csharp
#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

public class Veneers
{
    private readonly ILogger _logger;
```

Logger 初始化（line 22）：
```csharp
_logger = LoggingFactory.CreateLogger(typeof(Veneers).FullName);
```

日志调用变更：
- line 23: `_logger.Debug(` → `_logger.LogDebug(`
- line 54: `_logger.Error(` → `_logger.LogError(`
- line 76, 103: `_logger.Error(` → `_logger.LogError(`

- [ ] **Step 3: 提交 Veneer/Veneers 变更**

```bash
git add src/base-driver/base/Veneer.cs src/base-driver/base/Veneers.cs
git commit -m "feat: 替换 Veneer 和 Veneers 中的 NLog Logger 为 MEL ILogger"
```

---

### Task 6: 重构 Bootstrap.cs

**Files:**
- Modify: `src/base-driver/base/Bootstrap.cs`

- [ ] **Step 1: 重写 Bootstrap.cs — 移除 NLog，改用 MEL**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

// ReSharper disable once ClassNeverInstantiated.Global
public class Bootstrap
{
    private static ILogger _logger = NullLogger.Instance;

#pragma warning disable CS1998
    public static async Task Stop()
    {
        LoggingFactory.Close();
    }
#pragma warning restore CS1998

    public static async Task<dynamic> Start(string[] args)
    {
        DetectArch();
        var configFiles = GetArgument(args, "--config", "config.system.yml,config.user.yml,config.machines.yml");
        var config = ReadConfig(configFiles.Split(','));
        _logger = LoggingFactory.CreateLogger(typeof(Bootstrap).FullName);
        _logger.LogInformation("Configuration loaded");
        return config;
    }

    private static void DetectArch()
    {
        Console.WriteLine($"Bitness: {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}");
    }

    private static string GetArgument(string[] args, string optionName, string defaultValue)
    {
        var value = args.SkipWhile(i => i != optionName).Skip(1).Take(1).FirstOrDefault();
        var optionValue = string.IsNullOrEmpty(value) ? defaultValue : value;
        Console.WriteLine($"Argument '{optionName}' = '{optionValue}'");
        return optionValue;
    }

    private static dynamic ReadConfig(string[] configFiles)
    {
        var yaml = "";
        foreach (var configFile in configFiles) yaml += File.ReadAllText(configFile);

        var stringReader = new StringReader(yaml);
        var parser = new Parser(stringReader);
        var mergingParser = new MergingParser(parser);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var config = deserializer.Deserialize(mergingParser);

        _logger.LogTrace("Deserialized configuration: {Config}",
            JObject.FromObject(config ?? throw new InvalidOperationException("Configuration cannot be null.")));

        return config;
    }
}
```

变更要点：
- 移除 `using NLog`、`using NLog.Config`、`using NLog.Extensions.Logging`
- 新增 `using Microsoft.Extensions.Logging`、`using Microsoft.Extensions.Logging.Abstractions`
- `_logger` 类型改为 `ILogger`，默认 `NullLogger.Instance`
- 移除 `SetupLogger()` 方法
- 移除 `--nlog` 参数处理
- `Stop()` 中 `LogManager.Shutdown()` → `LoggingFactory.Close()`，加 `#pragma` 抑制 CS1998
- Logger 初始化移到 `ReadConfig` 之后
- `_logger.Trace(` → `_logger.LogTrace(`
- `Console.WriteLine` 保留

- [ ] **Step 2: 提交 Bootstrap 变更**

```bash
git add src/base-driver/base/Bootstrap.cs
git commit -m "refactor: 移除 Bootstrap 中的 NLog 配置，改为 MEL LoggingFactory"
```

---

### Task 7: 构建验证

- [ ] **Step 1: 构建所有项目**

```bash
dotnet build src/base-driver/base-driver.csproj --no-restore
dotnet build src/battery-driver/battery-driver.csproj --no-restore
dotnet build src/opcua-driver/opcua-driver.csproj --no-restore
dotnet build src/scanner-driver/scanner-driver.csproj --no-restore
dotnet build src/fins-driver/fins-driver.csproj --no-restore
dotnet build tests/battery-driver.test/battery-driver.test.csproj --no-restore
dotnet build tests/opcua-driver.test/opcua-driver.test.csproj --no-restore
dotnet build tests/scanner-driver.test/scanner-driver.test.csproj --no-restore
dotnet build tests/fins-driver.test/fins-driver.test.csproj --no-restore
```

期望结果：所有项目 BUILD 成功，无错误

- [ ] **Step 2: 运行测试**

```bash
dotnet test tests/battery-driver.test/battery-driver.test.csproj --no-restore
dotnet test tests/opcua-driver.test/opcua-driver.test.csproj --no-restore
dotnet test tests/scanner-driver.test/scanner-driver.test.csproj --no-restore
dotnet test tests/fins-driver.test/fins-driver.test.csproj --no-restore
```

期望结果：所有测试 PASS

- [ ] **Step 3: 最终提交**

```bash
git add -A
git commit -m "chore: 构建验证通过，完成日志框架替换"
```
