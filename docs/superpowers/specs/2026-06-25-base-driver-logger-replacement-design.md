# base-driver 日志框架替换设计

## 概述

将 base-driver 的日志框架从 **NLog 直接调用** 替换为 **Microsoft.Extensions.Logging.Abstractions**，彻底移除对 NLog 的直接依赖，由宿主应用程序决定具体日志提供程序，未配置时默认为 NullLogger（无日志输出）。

## 动机

- 解耦日志框架与库代码，遵循 .NET 生态标准抽象
- 宿主应用可自由选择日志提供程序（Console、Debug、EventLog、NLog、Serilog 等）
- 统一整个解决方案的日志抽象层
- 移除对 NLog 6.1.3 的版本依赖

## 变更清单

### NuGet 包依赖

#### base-driver.csproj

| 包 | 版本 | 操作 |
|---|---|---|
| `NLog` | 6.1.3 | ❌ 移除 |
| `NLog.Extensions.Logging` | 5.3.15 | ❌ 移除 |
| `Microsoft.Extensions.Configuration` | 8.0.0 | 保留 |
| `Microsoft.Extensions.Logging.Abstractions` | 8.0.0 | ✅ 新增 |

#### battery-driver.csproj

| 包 | 版本 | 操作 |
|---|---|---|
| `NLog` | 6.1.3 | ❌ 移除（死依赖，代码中未使用） |

其他驱动（opcua-driver、scanner-driver、fins-driver、drivers.common）无 NLog 依赖，无需变更。

### 新增文件

#### `src/base-driver/base/LoggingFactory.cs`

静态日志工厂类，封装 `ILoggerFactory` 的生命周期。

线程安全保证：
- `SetProvider` / `Close`：互斥锁保护写操作和资源释放
- `CreateLogger`：通过 `Volatile.Read` 确保 `_factory` 引用的可见性，避免 ARM64 或 JIT 重排序导致的读取过期引用

```csharp
namespace l99.driver.@base;

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
    /// 释放底层日志工厂（例如刷新缓冲、关闭文件等），并将工厂重置为 NullLogger。
    /// 宿主应在应用程序关闭时调用。重置后日志静默丢弃，防止关闭后误用。
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

### 修改文件

#### 1. `src/base-driver/base/LoggingFactory.cs`（新增）

见上节。

#### 2-8. 各基类的 Logger 替换

所有基类文件统一执行三处替换：

| 步骤 | 操作 |
|---|---|
| using 替换 | `using NLog;` → `using Microsoft.Extensions.Logging;` |
| 字段类型替换 | `NLog.ILogger` → `Microsoft.Extensions.Logging.ILogger` |
| 初始化替换 | `LogManager.GetCurrentClassLogger()` / `LogManager.GetLogger(GetType().FullName)` → `LoggingFactory.CreateLogger(...)` |

详细映射（每个文件替换所有出现次数）：

| 文件 | 出现次数 | 原调用 | 新调用 | Logger 类别 |
|---|---|---|---|---|
| `Machine.cs` | 1 处 | `LogManager.GetCurrentClassLogger()` | `LoggingFactory.CreateLogger(typeof(Machine).FullName)` | `l99.driver.@base.Machine` |
| `Machines.cs` | **2 处**（构造函数 + `CreateMachines` 局部变量） | `LogManager.GetCurrentClassLogger()` | `LoggingFactory.CreateLogger(typeof(Machines).FullName)` | `l99.driver.@base.Machines` |
| `Transport.cs` | 1 处 | `LogManager.GetLogger(GetType().FullName)` | `LoggingFactory.CreateLogger(GetType().FullName)` | 运行时派生类型 |
| `Strategy.cs` | 1 处 | `LogManager.GetLogger(GetType().FullName)` | `LoggingFactory.CreateLogger(GetType().FullName)` | 运行时派生类型 |
| `Handler.cs` | 1 处 | `LogManager.GetLogger(GetType().FullName)` | `LoggingFactory.CreateLogger(GetType().FullName)` | 运行时派生类型 |
| `Veneer.cs` | 1 处 | `LogManager.GetLogger(GetType().FullName)` | `LoggingFactory.CreateLogger(GetType().FullName)` | 运行时派生类型 |
| `Veneers.cs` | 1 处 | `LogManager.GetCurrentClassLogger()` | `LoggingFactory.CreateLogger(typeof(Veneers).FullName)` | `l99.driver.@base.Veneers` |

**API 兼容性说明**：NLog 的 `ILogger.Trace()`/`Debug()`/`Info()`/`Error()`/`Warn()` → MEL 的 `LogTrace()`/`LogDebug()`/`LogInformation()`/`LogError()`/`LogWarning()` 扩展方法。由于 MEL 的日志级别扩展方法属于 `Microsoft.Extensions.Logging.LoggerExtensions`，自动随 `using Microsoft.Extensions.Logging;` 引入。对于消息模板语法，需将 NLog 风格的 `$"..."` 字符串插值替换为 MEL 的模板格式化：`$"Message {value}"` → `"Message {Value}", value`。

但是，由于代码库中使用的是字符串插值（`$"{variable}"`）而非 NLog 模板语法，本次**不强制改为模板语法**，保留现有字符串插值方式以最小化变更范围。例如：

```
// 原代码
_logger.Info($"[{configuration.machine.id}] Machine disabled and will not be added");

// 新代码
_logger.LogInformation("[{MachineId}] Machine disabled and will not be added", configuration.machine.id);
```

vs 保留插值（本次采用）：

```
_logger.LogInformation($"[{configuration.machine.id}] Machine disabled and will not be added");
```

**本次采用保留插值方案**，以最小化变更范围。

#### 9. `src/base-driver/base/Bootstrap.cs`

| 变更 | 说明 |
|---|---|
| 移除 `using NLog`、`using NLog.Config`、`using NLog.Extensions.Logging` | 不再依赖 NLog |
| 移除 `SetupLogger()` 方法 | 日志由宿主管理 |
| 移除 `--nlog` CLI 参数处理 | 不再需要指定 NLog 配置文件 |
| `Stop()` 中 `LogManager.Shutdown()` → `LoggingFactory.Close()` | 释放日志工厂资源。方法签名保持 `public static async Task Stop()`，使用 `#pragma warning disable CS1998` 抑制警告，因现有调用者可能使用 `await` |
| `_logger` 类型改为 `Microsoft.Extensions.Logging.ILogger`，默认 `NullLogger.Instance` | 宿主未配置时无日志 |
| 初始化 Logger 移到配置加载之后：`_logger = LoggingFactory.CreateLogger(typeof(Bootstrap).FullName)` | 配置完成后创建 Logger |
| 将 `_logger.Trace(...)` 改为 `_logger.LogTrace(...)` | MEL 扩展方法 |
| `Console.WriteLine` 保留 | 用于 CLI 参数解析阶段的诊断输出 |

### 不变更

- **所有构造函数签名** — 不引入 `ILogger<T>` 或 `ILoggerFactory` 参数，保持反射实例化的兼容性
- **所有 `Activator.CreateInstance` 调用** — 无需调整参数
- **派生驱动项目** — battery-driver、opcua-driver、scanner-driver、fins-driver 的源代码无需修改
- **日志类别名称语义** — 完全保持原有行为（编译期类型 vs 运行时类型）
- **`Console.WriteLine`** — Bootstrap.cs 中的 `DetectArch()` 和 `GetArgument()` 保留

## 测试策略

- 编译验证：所有项目编译通过
- 运行时行为验证：
  - 未调用 `LoggingFactory.SetProvider()` 时，日志静默丢弃
  - 调用 `LoggingFactory.SetProvider(LoggerFactory.Create(b => b.AddConsole()))` 后，日志正常输出到 Console
  - 调用 `LoggingFactory.Close()` 后资源正常释放，再次调用日志静默丢弃
  - 重复调用 `SetProvider()` 不抛异常，旧工厂被正确释放
  - 调用 `SetProvider` → `Close` → `SetProvider` 顺序正常
  - 并发场景：从多个线程同时调用 `CreateLogger`，不会读到空引用或已释放的工厂

## 迁移说明

本次变更为向后不兼容变更，已有使用者需要注意以下事项：

### `--nlog` 参数移除

Bootstrap 不再识别 `--nlog` CLI 参数，也不再自动加载 `nlog.config` 文件。使用该参数的启动脚本需要移除参数，改为通过 `LoggingFactory.SetProvider()` 在代码中配置日志。

### `nlog.config` 文件不再生效

如果现有部署依赖 `nlog.config` 配置文件，需要：
1. 移除项目中的 `nlog.config` 文件（保留也不会报错，但不再生效）
2. 在宿主入口代码中使用 `LoggingFactory.SetProvider(LoggerFactory.Create(b => b.AddNLog()))` 并配置 NLog 的 `LogManager.Setup()`，如果仍想使用 NLog 作为底层实现

### 编译影响

- base-driver：需要更新 csproj 引用和代码中的 Logger 调用
- battery-driver：可移除 `NLog` 包引用（死依赖），但非必须
- 其他派生驱动（opcua-driver、scanner-driver、fins-driver）：无需修改，重新编译即可

## 宿主集成方式

宿主应用在调用 `Bootstrap.Start()` 前配置日志：

```csharp
// 宿主应用入口
// 注意：不用 using，由 LoggingFactory.Close() / Bootstrap.Stop() 负责释放
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.AddDebug();
    // 或 builder.AddNLog(); 如果宿主仍想用 NLog 作为底层实现
});
LoggingFactory.SetProvider(loggerFactory);

var config = await Bootstrap.Start(args);
var machines = await Machines.CreateMachines(config);
await machines.RunAsync(stoppingToken);
await Bootstrap.Stop();  // 内部调用 LoggingFactory.Close() 释放 loggerFactory
```
