# DI 引入实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 collection-drivers 从反射+static 定位器迁移到 Microsoft.Extensions.DependencyInjection

**Architecture:** DI 负责基础设施(ILogger)，ActivatorUtilities 负责组件组装。每台机器独立 IServiceScope，5个Phase增量迁移

**Tech Stack:** .NET 8, Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0, Microsoft.Extensions.Configuration.Binder 8.0.0, Moq 4.20.70

## Global Constraints

- 所有公开类型/属性/方法必须包含中文 XML 文档注释（CLAUDE.md 规范 1）
- 枚举成员显式指定整数值（CLAUDE.md 规范 2）
- Git 提交格式 `type: 中文描述`（CLAUDE.md 规范 3）
- 业务逻辑变更须 TDD：RED→GREEN→REFACTOR（CLAUDE.md 规范 4）
- 纯机械性工作（DTO/接口/常量）跳过 TDD，声明理由
- 遵守 YAGNI 原则
- 每个 Phase 编译通过，现有测试全部通过
- Nullable enable 全部项目

---

## Phase 1：配置 DTO + 类型注册基础设施（非破坏性）

> TDD 跳过理由：Phase 1 为纯 DTO/接口/扩展方法脚手架，无业务逻辑

### Task 1.1: 新增 NuGet 包引用

**Files:**
- Modify: `src/CollectionDrivers.Common/CollectionDrivers.Common.csproj`

**Interfaces:**
- Produces: `Microsoft.Extensions.DependencyInjection.Abstractions` 8.0.0 可用, `Microsoft.Extensions.Configuration.Binder` 8.0.0 可用

- [ ] **Step 1: 添加 PackageReference**

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="8.0.0" />
```

在已有 `<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="8.0.0" />` 之后添加。

- [ ] **Step 2: 还原包并确认编译**

Run: `dotnet restore src/CollectionDrivers.Common/CollectionDrivers.Common.csproj`
Expected: 无错误

Run: `dotnet build src/CollectionDrivers.Common/CollectionDrivers.Common.csproj --no-restore`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/CollectionDrivers.Common/CollectionDrivers.Common.csproj
git commit -m "chore: 添加 DI 和 Configuration.Binder NuGet 引用"
```

---

### Task 1.2: 创建配置 DTO

**Files:**
- Create: `src/CollectionDrivers.Common/CollectionDriverOptions.cs`
- Create: `src/CollectionDrivers.Common/MachineOptions.cs`

**Interfaces:**
- Produces: `CollectionDriverOptions` 类 (含 `List<MachineOptions> Machines`), `MachineOptions` 类 (含 `Id`, `Enabled`, `Type`, `DriverId`, `SweepMs`, `Configuration`)

- [ ] **Step 1: 创建 CollectionDriverOptions.cs**

```csharp
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动库顶层配置，从宿主 IConfiguration 绑定。
/// </summary>
public class CollectionDriverOptions
{
    /// <summary>机器列表</summary>
    public List<MachineOptions> Machines { get; set; } = new();
}
```

- [ ] **Step 2: 创建 MachineOptions.cs**

```csharp
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的配置。
/// </summary>
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
    /// </summary>
    public string? DriverId { get; set; }

    /// <summary>采集间隔（毫秒），对应旧 type.sweep_ms</summary>
    public int SweepMs { get; set; } = 5000;

    /// <summary>
    /// 当前机器对应的 IConfiguration 段引用。
    /// 不通过 .Bind() 填充——由 DriverHostService 在运行时手动注入。
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public IConfiguration? Configuration { get; set; }
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build src/CollectionDrivers.Common/CollectionDrivers.Common.csproj --no-restore`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/CollectionDriverOptions.cs src/CollectionDrivers.Common/MachineOptions.cs
git commit -m "feat: 添加 CollectionDriverOptions 和 MachineOptions 配置 DTO"
```

---

### Task 1.3: 创建各驱动 Options DTO

**Files:**
- Create: `src/CollectionDrivers.BatteryDriver/models/BatteryTcpStrategyOptions.cs`
- Create: `src/CollectionDrivers.FinsDriver/models/FinsStrategyOptions.cs`
- Create: `src/CollectionDrivers.OpcUaDriver/models/OpcUaStrategyOptions.cs`
- Create: `src/CollectionDrivers.ScannerDriver/models/ScannerStrategyOptions.cs`
- Create: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransportOptions.cs`

**Interfaces:**
- Produces: 5 个 Options DTO 类，各含默认值和 `[ConfigurationKeyName]` 标注

- [ ] **Step 1: 创建 BatteryTcpStrategyOptions.cs**

```csharp
namespace CollectionDrivers.BatteryDriver.Models;

/// <summary>
/// Battery TCP 策略配置。
/// </summary>
public class BatteryTcpStrategyOptions
{
    /// <summary>数据端口</summary>
    [System.Text.Json.Serialization.JsonPropertyName("port")]
    public int Port { get; set; } = 13000;

    /// <summary>告警端口</summary>
    [System.Text.Json.Serialization.JsonPropertyName("warning_port")]
    public int WarningPort { get; set; } = 13100;

    /// <summary>心跳超时（秒）</summary>
    [System.Text.Json.Serialization.JsonPropertyName("heartbeat_timeout_s")]
    public int HeartbeatTimeoutS { get; set; } = 60;
}
```

- [ ] **Step 2: 创建 FinsStrategyOptions.cs**

```csharp
namespace CollectionDrivers.FinsDriver.Models;

/// <summary>
/// FINS UDP 策略配置。
/// </summary>
public class FinsStrategyOptions
{
    /// <summary>远程 IP 地址</summary>
    public string RemoteIp { get; set; } = "192.168.1.1";

    /// <summary>端口号</summary>
    public int Port { get; set; } = 9600;

    /// <summary>超时（毫秒）</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>采集器列表</summary>
    public FinsCollectorConfig[] Collectors { get; set; } = Array.Empty<FinsCollectorConfig>();
}

/// <summary>FINS 采集器配置项</summary>
public class FinsCollectorConfig
{
    /// <summary>采集器名称</summary>
    public string Name { get; set; } = "";

    /// <summary>起始地址</summary>
    public ushort StartAddress { get; set; }

    /// <summary>读取长度（字）</summary>
    public ushort Length { get; set; }
}
```

- [ ] **Step 3: 创建 OpcUaStrategyOptions.cs**

```csharp
namespace CollectionDrivers.OpcUaDriver.Models;

/// <summary>
/// OPC UA 策略配置。
/// </summary>
public class OpcUaStrategyOptions
{
    /// <summary>OPC UA 端点 URL</summary>
    public string Endpoint { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>是否启用安全连接</summary>
    public bool UseSecurity { get; set; }

    /// <summary>重连周期（毫秒）</summary>
    public int ReconnectPeriodMs { get; set; } = 10000;

    /// <summary>是否自动接受证书</summary>
    public bool AutoAcceptCerts { get; set; } = true;

    /// <summary>用户名（可选）</summary>
    public string? UserName { get; set; }

    /// <summary>密码（可选）</summary>
    public string? Password { get; set; }

    /// <summary>采集器列表</summary>
    public OpcUaCollectorConfig[] Collectors { get; set; } = Array.Empty<OpcUaCollectorConfig>();
}

/// <summary>OPC UA 采集器配置项</summary>
public class OpcUaCollectorConfig
{
    /// <summary>采集器名称</summary>
    public string Name { get; set; } = "";

    /// <summary>采集模式：subscription 或 poll</summary>
    public string Mode { get; set; } = "subscription";

    /// <summary>采样间隔（毫秒）</summary>
    public int SamplingIntervalMs { get; set; } = 100;

    /// <summary>轮询间隔（毫秒），仅 poll 模式</summary>
    public int? SweepIntervalMs { get; set; }

    /// <summary>节点列表</summary>
    public OpcUaNodeConfig[] Nodes { get; set; } = Array.Empty<OpcUaNodeConfig>();
}

/// <summary>OPC UA 节点配置</summary>
public class OpcUaNodeConfig
{
    /// <summary>节点 ID</summary>
    public string Id { get; set; } = "";

    /// <summary>别名</summary>
    public string? Alias { get; set; }
}
```

- [ ] **Step 4: 创建 ScannerStrategyOptions.cs**

```csharp
namespace CollectionDrivers.ScannerDriver.Models;

/// <summary>
/// 扫描枪策略配置。
/// </summary>
public class ScannerStrategyOptions
{
    /// <summary>主机地址</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>端口号</summary>
    public int Port { get; set; } = 2000;

    /// <summary>工作模式：sync 或 async</summary>
    public string Mode { get; set; } = "sync";

    /// <summary>重试次数</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>连接超时（毫秒）</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;

    /// <summary>接收超时（毫秒）</summary>
    public int ReceiveTimeoutMs { get; set; } = 5000;

    /// <summary>是否启用去重</summary>
    public bool DedupEnabled { get; set; }

    /// <summary>协议配置</summary>
    public ScannerProtocolOptions Protocol { get; set; } = new();
}

/// <summary>扫描枪协议配置</summary>
public class ScannerProtocolOptions
{
    /// <summary>发送命令（十六进制字符串）</summary>
    public string SendCommandHex { get; set; } = "";

    /// <summary>响应编码</summary>
    public string ResponseEncoding { get; set; } = "ascii";

    /// <summary>条码正则表达式</summary>
    public string? BarcodeRegex { get; set; }

    /// <summary>正则匹配组索引</summary>
    public int RegexGroupIndex { get; set; }

    /// <summary>帧分隔符（十六进制字符串）</summary>
    public string? FrameDelimiterHex { get; set; }

    /// <summary>需移除的前缀列表</summary>
    public string[] RemovePrefixes { get; set; } = Array.Empty<string>();

    /// <summary>需移除的后缀列表</summary>
    public string[] RemoveSuffixes { get; set; } = Array.Empty<string>();
}
```

- [ ] **Step 5: 创建 InfluxDbTransportOptions.cs**

```csharp
namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB 传输层配置。
/// </summary>
public class InfluxDbTransportOptions
{
    /// <summary>InfluxDB 主机地址</summary>
    public string Host { get; set; } = "http://localhost:8086";

    /// <summary>认证 Token</summary>
    public string Token { get; set; } = "";

    /// <summary>Bucket 名称</summary>
    public string Bucket { get; set; } = "default";

    /// <summary>组织名称</summary>
    public string Org { get; set; } = "default";

    /// <summary>Scriban 模板变换器映射：模板名 → 模板文本</summary>
    public Dictionary<string, string> Transformers { get; set; } = new();
}
```

- [ ] **Step 6: 编译验证**

Run: `dotnet build`
Expected: Build succeeded (所有项目编译通过)

- [ ] **Step 7: Commit**

```bash
git add src/CollectionDrivers.BatteryDriver/models/BatteryTcpStrategyOptions.cs \
        src/CollectionDrivers.FinsDriver/models/FinsStrategyOptions.cs \
        src/CollectionDrivers.OpcUaDriver/models/OpcUaStrategyOptions.cs \
        src/CollectionDrivers.ScannerDriver/models/ScannerStrategyOptions.cs \
        src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransportOptions.cs
git commit -m "feat: 添加各驱动和 Transport 的 Options DTO"
```

---

### Task 1.4: 创建接口定义

**Files:**
- Create: `src/CollectionDrivers.Common/IHandler.cs`
- Create: `src/CollectionDrivers.Common/IMachineContext.cs`
- Create: `src/CollectionDrivers.Common/IMachineScope.cs`
- Create: `src/CollectionDrivers.Common/IMachineScopeFactory.cs`

**Interfaces:**
- Produces: `IHandler`, `IMachineContext`, `IMachineScope`, `IMachineScopeFactory` 接口

- [ ] **Step 1: 创建 IHandler.cs**

```csharp
namespace CollectionDrivers.Common;

/// <summary>
/// 采集完成后的数据处理契约。Strategy 每次 Sweep 完成后调用。
/// </summary>
public interface IHandler
{
    /// <summary>采集周期完成时调用</summary>
    Task OnStrategySweepCompleteInternalAsync();
}
```

- [ ] **Step 2: 创建 IMachineContext.cs**

```csharp
namespace CollectionDrivers.Common;

/// <summary>
/// Strategy/Handler/Transport 对 Machine 的只读视图。
/// 切断 Machine ↔ Strategy 循环依赖。
/// </summary>
public interface IMachineContext
{
    /// <summary>机器标识符</summary>
    string Id { get; }

    /// <summary>是否启用</summary>
    bool Enabled { get; }

    /// <summary>采集间隔（毫秒）</summary>
    int SweepMs { get; }

    /// <summary>数据处理组件</summary>
    IHandler Handler { get; }

    /// <summary>所有已注册的数据发送组件</summary>
    IReadOnlyList<Transport> Transports { get; }

    /// <summary>Strategy 上次采集是否成功</summary>
    bool StrategySuccess { get; }

    /// <summary>Strategy 当前是否健康</summary>
    bool StrategyHealthy { get; }

    /// <summary>停止设备，运行中的采集循环由此退出</summary>
    Task Stop();
}
```

- [ ] **Step 3: 创建 IMachineScope.cs**

```csharp
namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的采集 Scope。封装 IServiceScope + 组件生命周期。
/// </summary>
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

- [ ] **Step 4: 创建 IMachineScopeFactory.cs**

```csharp
namespace CollectionDrivers.Common;

/// <summary>
/// 创建 MachineScope 的工厂。由 DI 容器注册为 Singleton。
/// </summary>
public interface IMachineScopeFactory
{
    /// <summary>为指定机器配置创建独立的采集 Scope</summary>
    IMachineScope CreateScope(MachineOptions config);
}
```

- [ ] **Step 5: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/CollectionDrivers.Common/IHandler.cs \
        src/CollectionDrivers.Common/IMachineContext.cs \
        src/CollectionDrivers.Common/IMachineScope.cs \
        src/CollectionDrivers.Common/IMachineScopeFactory.cs
git commit -m "feat: 添加 IHandler、IMachineContext、IMachineScope、IMachineScopeFactory 接口"
```

---

### Task 1.5: 创建 DriverTypeRegistry

**Files:**
- Create: `src/CollectionDrivers.Common/DriverTypeRegistry.cs`

**Interfaces:**
- Produces: `DriverTypeRegistry` 类 (含 `Entries: List<Entry>`, `Find(string? driverId): Entry?`), `DriverTypeRegistry.Entry` record (含 `DriverId`, `MachineType`, `StrategyType`, `HandlerType`, `TransportType`, `StrategyOptionsType`, `TransportOptionsType`)

- [ ] **Step 1: 创建 DriverTypeRegistry.cs**

```csharp
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动类型注册表（Singleton）。各驱动的 Add 扩展方法在此注册类型元数据。
/// MachineScope 创建时通过 MachineOptions.DriverId 查找匹配条目。
/// </summary>
public class DriverTypeRegistry
{
    /// <summary>已注册的驱动条目列表</summary>
    public readonly List<Entry> Entries = new();

    /// <summary>
    /// 按 DriverId 精确匹配注册条目。若 driverId 为 null，返回第一个条目。
    /// 重复 DriverId 注册时 first-wins，不抛异常但应由调用方输出日志警告。
    /// </summary>
    public Entry? Find(string? driverId)
    {
        if (driverId != null)
            return Entries.FirstOrDefault(e => e.DriverId == driverId);
        return Entries.FirstOrDefault();
    }

    /// <summary>驱动类型注册条目</summary>
    public sealed record Entry(
        /// <summary>驱动标识符。Add*Driver 扩展方法设置，用于多驱动区分。</summary>
        string DriverId,
        /// <summary>Machine 具体类型</summary>
        Type MachineType,
        /// <summary>Strategy 具体类型</summary>
        Type StrategyType,
        /// <summary>Handler 具体类型</summary>
        Type HandlerType,
        /// <summary>Transport 具体类型</summary>
        Type TransportType,
        /// <summary>Strategy Options 类型</summary>
        Type StrategyOptionsType,
        /// <summary>Transport Options 类型</summary>
        Type TransportOptionsType
    );
}
```

- [ ] **Step 2: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/CollectionDrivers.Common/DriverTypeRegistry.cs
git commit -m "feat: 添加 DriverTypeRegistry 驱动类型注册表"
```

---

### Task 1.6: 创建 ServiceCollectionExtensions

**Files:**
- Create: `src/CollectionDrivers.Common/ServiceCollectionExtensions.cs`
- Create: `src/CollectionDrivers.BatteryDriver/BatteryDriverServiceExtensions.cs`
- Create: `src/CollectionDrivers.FinsDriver/FinsDriverServiceExtensions.cs`
- Create: `src/CollectionDrivers.OpcUaDriver/OpcUaDriverServiceExtensions.cs`
- Create: `src/CollectionDrivers.ScannerDriver/ScannerDriverServiceExtensions.cs`
- Create: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransportServiceExtensions.cs`

**Interfaces:**
- Produces: `AddCollectionDrivers(IServiceCollection, IConfiguration)` 扩展方法, 各 `Add*Driver()` 扩展方法

- [ ] **Step 1: 创建 ServiceCollectionExtensions.cs**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// IServiceCollection 扩展方法。宿主调用以注册驱动基础设施。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册驱动基础设施：绑定配置、注册 IMachineScopeFactory、注册 DriverHostService。
    /// 注意：Phase 4 前 IMachineScopeFactory 实现为占位（抛 NotImplementedException）。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configurationSection">"CollectionDrivers" 配置段</param>
    public static IServiceCollection AddCollectionDrivers(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        services.Configure<CollectionDriverOptions>(configurationSection);

        // Phase 4 前为占位实现
        services.AddSingleton<IMachineScopeFactory, MachineScopeFactoryPlaceholder>();

        services.AddHostedService<DriverHostService>();

        return services;
    }
}

/// <summary>
/// Phase 4 前的占位实现，防止宿主启动失败。
/// CreateScope 抛出 NotSupportedException。
/// </summary>
internal class MachineScopeFactoryPlaceholder : IMachineScopeFactory
{
    public IMachineScope CreateScope(MachineOptions config)
        => throw new NotSupportedException(
            "DI migration not yet complete. MachineScope will be available in Phase 4.");
}
```

- [ ] **Step 2: 创建 BatteryDriverServiceExtensions.cs**

```csharp
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using CollectionDrivers.Transport.InfluxDB;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// Battery 驱动注册扩展。
/// </summary>
public static class BatteryDriverServiceExtensions
{
    /// <summary>注册 Battery 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddBatteryDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:             "Battery",
            MachineType:          typeof(Machine),
            StrategyType:         typeof(BatteryTcpStrategy),
            HandlerType:          typeof(TransportHandler),
            TransportType:        typeof(InfluxDbTransport),
            StrategyOptionsType:  typeof(BatteryTcpStrategyOptions),
            TransportOptionsType: typeof(InfluxDbTransportOptions)
        ));
        return services;
    }
}
```

- [ ] **Step 3: 创建 FinsDriverServiceExtensions.cs**

```csharp
using CollectionDrivers.FinsDriver.Models;
using CollectionDrivers.FinsDriver.Strategies;
using CollectionDrivers.Transport.InfluxDB;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// FINS 驱动注册扩展。
/// </summary>
public static class FinsDriverServiceExtensions
{
    /// <summary>注册 FINS 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddFinsDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:             "Fins",
            MachineType:          typeof(Machine),
            StrategyType:         typeof(FinsStrategy),
            HandlerType:          typeof(TransportHandler),
            TransportType:        typeof(InfluxDbTransport),
            StrategyOptionsType:  typeof(FinsStrategyOptions),
            TransportOptionsType: typeof(InfluxDbTransportOptions)
        ));
        return services;
    }
}
```

- [ ] **Step 4: 创建 OpcUaDriverServiceExtensions.cs**

```csharp
using CollectionDrivers.OpcUaDriver.Models;
using CollectionDrivers.OpcUaDriver.Strategies;
using CollectionDrivers.Transport.InfluxDB;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// OPC UA 驱动注册扩展。
/// </summary>
public static class OpcUaDriverServiceExtensions
{
    /// <summary>注册 OPC UA 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddOpcUaDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:             "OpcUa",
            MachineType:          typeof(Machine),
            StrategyType:         typeof(OpcUaStrategy),
            HandlerType:          typeof(TransportHandler),
            TransportType:        typeof(InfluxDbTransport),
            StrategyOptionsType:  typeof(OpcUaStrategyOptions),
            TransportOptionsType: typeof(InfluxDbTransportOptions)
        ));
        return services;
    }
}
```

- [ ] **Step 5: 创建 ScannerDriverServiceExtensions.cs**

```csharp
using CollectionDrivers.ScannerDriver.Models;
using CollectionDrivers.ScannerDriver.Strategies;
using CollectionDrivers.Transport.InfluxDB;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// Scanner 驱动注册扩展。
/// </summary>
public static class ScannerDriverServiceExtensions
{
    /// <summary>注册 Scanner 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddScannerDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:             "Scanner",
            MachineType:          typeof(Machine),
            StrategyType:         typeof(ScannerStrategy),
            HandlerType:          typeof(TransportHandler),
            TransportType:        typeof(InfluxDbTransport),
            StrategyOptionsType:  typeof(ScannerStrategyOptions),
            TransportOptionsType: typeof(InfluxDbTransportOptions)
        ));
        return services;
    }
}
```

- [ ] **Step 6: 创建 InfluxDbTransportServiceExtensions.cs**

```csharp
namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB Transport 注册扩展（独立于驱动注册，供宿主自定义 Transport 时使用）。
/// 当前各驱动的 Add*Driver 已将 Transport 类型内嵌在 Entry 中，
/// 此方法作为宿主独立注册 Transport 的入口点保留。
/// </summary>
public static class InfluxDbTransportServiceExtensions
{
    // 当前 Transport 类型已嵌入各 Add*Driver 的 Entry 中，暂无需独立扩展
}
```

- [ ] **Step 7: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 8: Commit**

```bash
git add src/CollectionDrivers.Common/ServiceCollectionExtensions.cs \
        src/CollectionDrivers.BatteryDriver/BatteryDriverServiceExtensions.cs \
        src/CollectionDrivers.FinsDriver/FinsDriverServiceExtensions.cs \
        src/CollectionDrivers.OpcUaDriver/OpcUaDriverServiceExtensions.cs \
        src/CollectionDrivers.ScannerDriver/ScannerDriverServiceExtensions.cs \
        src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransportServiceExtensions.cs
git commit -m "feat: 添加 ServiceCollection 扩展方法和驱动注册扩展"
```

---

### Task 1.7: Phase 1 集成验证

**Files:**
- Create: `tests/battery-driver.test/options/OptionsBindingTest.cs`

**Interfaces:**
- Consumes: `CollectionDriverOptions`, `MachineOptions`, `BatteryTcpStrategyOptions`
- Produces: Options 绑定正确性验证测试

- [ ] **Step 1: 创建 OptionsBindingTest.cs**

```csharp
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.Common;
using Microsoft.Extensions.Configuration;

namespace battery_driver.test.Options;

/// <summary>
/// 验证 Configuration DTO 的 IConfiguration.Bind() 行为。
/// TDD 跳过理由：纯 DTO 绑定行为验证，属配置测试非业务逻辑。
/// </summary>
public class OptionsBindingTest
{
    [Fact]
    public void BatteryTcpOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Port"] = "12345",
                ["WarningPort"] = "12346",
                ["HeartbeatTimeoutS"] = "120"
            }).Build();

        var options = new BatteryTcpStrategyOptions();
        config.Bind(options);

        Assert.Equal(12345, options.Port);
        Assert.Equal(12346, options.WarningPort);
        Assert.Equal(120, options.HeartbeatTimeoutS);
    }

    [Fact]
    public void BatteryTcpOptions_HasCorrectDefaults()
    {
        var options = new BatteryTcpStrategyOptions();

        Assert.Equal(13000, options.Port);
        Assert.Equal(13100, options.WarningPort);
        Assert.Equal(60, options.HeartbeatTimeoutS);
    }

    [Fact]
    public void MachineOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Machines:0:Id"] = "test-machine",
                ["Machines:0:Enabled"] = "true",
                ["Machines:0:DriverId"] = "Battery",
                ["Machines:0:SweepMs"] = "3000"
            }).Build();

        var options = new CollectionDriverOptions();
        config.Bind(options);

        Assert.Single(options.Machines);
        Assert.Equal("test-machine", options.Machines[0].Id);
        Assert.True(options.Machines[0].Enabled);
        Assert.Equal("Battery", options.Machines[0].DriverId);
        Assert.Equal(3000, options.Machines[0].SweepMs);
    }
}
```

- [ ] **Step 2: 运行测试**

Run: `dotnet test tests/battery-driver.test/battery-driver.test.csproj --filter "FullyQualifiedName~OptionsBindingTest"`
Expected: 3 tests passed

- [ ] **Step 3: 运行全部现有测试确认无回归**

Run: `dotnet test`
Expected: All tests passed (Phase 1 为非破坏性变更)

- [ ] **Step 4: Commit**

```bash
git add tests/battery-driver.test/options/OptionsBindingTest.cs
git commit -m "test: 添加 Options 绑定验证测试"
```

---

### Phase 1 检查点

- [ ] `dotnet build` 全部项目编译通过
- [ ] `dotnet test` 全部测试通过
- [ ] 旧 YAML→dynamic 路径正常运行（未受影响）
- [ ] Phase 1 PR 可合并

---

## Phase 2：构造函数 ILogger 可选参数（向后兼容）

> TDD 策略：构造函数变更是机械性签名变更，不涉及新业务逻辑。对每个变更类：先写验证构造成功的测试→改构造函数→测试通过。存量测试保持通过的前提是旧构造函数仍可用。

### Task 2.1: Strategy 基类添加 ILogger 构造函数

**Files:**
- Modify: `src/CollectionDrivers.Common/Strategy.cs`
- Test: `tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs` (追加测试)

**Interfaces:**
- Produces: `Strategy(ILogger?, IMachineContext)` 新增构造函数（Phase 2 使用 Machine 类型）, `[Obsolete] Strategy(Machine)` 保留

- [ ] **Step 1: 写失败测试**

在 `BatteryTcpStrategyTest.cs` 追加：

```csharp
[Fact]
public void Constructor_WithILoggerAndMachine_CreatesSuccessfully()
{
    var logger = new NullLogger<BatteryTcpStrategy>();
    var machines = Machines.CreatePlaceholder();
    dynamic config = new ExpandoObject();
    config.machine = new ExpandoObject();
    config.machine.id = "test";
    config.machine.enabled = true;
    config.type = new ExpandoObject();
    config.type.sweep_ms = 5000;

    var machine = new BatteryMachine(machines, config);
    var strategy = new BatteryTcpStrategy(logger, machine, new BatteryTcpStrategyOptions());

    Assert.NotNull(strategy);
    Assert.Equal(machine, strategy.Machine);
}
```

Run: `dotnet test tests/battery-driver.test --filter "Constructor_WithILoggerAndMachine"`
Expected: FAIL — `BatteryTcpStrategy` 没有 `(ILogger, Machine, BatteryTcpStrategyOptions)` 构造函数

- [ ] **Step 2: 修改 Strategy.cs 添加新构造函数**

```csharp
// 在现有 protected Strategy(Machine machine) 构造函数下方添加：

/// <summary>
/// 构造策略实例（DI 注入 Logger + 机器上下文）。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
protected Strategy(ILogger? logger, Machine machine)
{
    Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
    Machine = machine;
    SweepMs = machine.Configuration.type["sweep_ms"];
}
```

- [ ] **Step 3: 修改 BatteryTcpStrategy.cs 添加新构造函数**

```csharp
// 在旧构造函数下方添加：
private readonly BatteryTcpStrategyOptions? _options;

/// <summary>
/// DI 构造函数：ILogger + Machine + 驱动专用 Options。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
public BatteryTcpStrategy(
    ILogger? logger,
    Machine machine,
    BatteryTcpStrategyOptions options) : base(logger, machine)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
}
```

- [ ] **Step 4: 运行新测试确认通过**

Run: `dotnet test tests/battery-driver.test --filter "Constructor_WithILoggerAndMachine"`
Expected: 1 test passed

- [ ] **Step 5: 运行全部现有测试确认无回归**

Run: `dotnet test`
Expected: All tests passed (旧构造函数仍可用)

- [ ] **Step 6: Commit**

```bash
git add src/CollectionDrivers.Common/Strategy.cs \
        src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs \
        tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs
git commit -m "feat: Strategy 基类和 BatteryTcpStrategy 添加 ILogger 可选构造函数"
```

---

### Task 2.2: Handler 基类和 TransportHandler 添加 ILogger 构造函数

**Files:**
- Modify: `src/CollectionDrivers.Common/Handler.cs`
- Modify: `src/CollectionDrivers.Common/TransportHandler.cs`

**Interfaces:**
- Produces: `Handler(ILogger?, Machine)` 新增, `TransportHandler(ILogger?, Machine)` 新增

- [ ] **Step 1: 修改 Handler.cs**

在现有 `protected Handler(Machine machine)` 下方添加：

```csharp
/// <summary>
/// 构造 Handler（DI 注入 Logger + 机器上下文）。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
protected Handler(ILogger? logger, Machine machine)
{
    Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
    Machine = machine;
}
```

- [ ] **Step 2: 修改 TransportHandler.cs**

在现有构造函数下方添加：

```csharp
/// <summary>
/// DI 构造函数：ILogger + Machine。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
public TransportHandler(ILogger? logger, Machine machine)
    : base(logger, machine) { }
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: 运行全部测试**

Run: `dotnet test`
Expected: All tests passed

- [ ] **Step 5: Commit**

```bash
git add src/CollectionDrivers.Common/Handler.cs \
        src/CollectionDrivers.Common/TransportHandler.cs
git commit -m "feat: Handler 和 TransportHandler 添加 ILogger 可选构造函数"
```

---

### Task 2.3: Transport 基类和 InfluxDbTransport 添加 ILogger 构造函数

**Files:**
- Modify: `src/CollectionDrivers.Common/Transport.cs`
- Modify: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs`

**Interfaces:**
- Produces: `Transport(ILogger?, Machine)` 新增, `InfluxDbTransport(ILogger?, Machine, InfluxDbTransportOptions)` 新增

- [ ] **Step 1: 修改 Transport.cs**

在现有 `protected Transport(Machine machine)` 下方添加：

```csharp
/// <summary>
/// 构造 Transport（DI 注入 Logger + 机器上下文）。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
protected Transport(ILogger? logger, Machine machine)
{
    Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
    Machine = machine;
}
```

- [ ] **Step 2: 修改 InfluxDbTransport.cs**

在旧构造函数下方添加：

```csharp
private readonly InfluxDbTransportOptions? _options;

/// <summary>
/// DI 构造函数：ILogger + Machine + Transport Options。
/// Phase 2 使用 Machine，Phase 3 改为 IMachineContext。
/// </summary>
public InfluxDbTransport(
    ILogger? logger,
    Machine machine,
    InfluxDbTransportOptions options) : base(logger, machine)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: 运行全部测试**

Run: `dotnet test`
Expected: All tests passed

- [ ] **Step 5: Commit**

```bash
git add src/CollectionDrivers.Common/Transport.cs \
        src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs
git commit -m "feat: Transport 基类和 InfluxDbTransport 添加 ILogger 可选构造函数"
```

---

### Task 2.4: FinsStrategy、OpcUaStrategy、ScannerStrategy 添加 ILogger 构造函数

**Files:**
- Modify: `src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs`
- Modify: `src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs`
- Modify: `src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs`

**Interfaces:**
- Produces: 三个 Strategy 子类各新增 `(ILogger?, Machine, *Options)` 构造函数

- [ ] **Step 1: 修改 FinsStrategy.cs**

在旧构造函数下方添加：

```csharp
private readonly FinsStrategyOptions? _options;

/// <summary>
/// DI 构造函数：ILogger + Machine + FINS Options。
/// </summary>
public FinsStrategy(
    ILogger? logger,
    Machine machine,
    FinsStrategyOptions options) : base(logger, machine)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
}
```

- [ ] **Step 2: 修改 OpcUaStrategy.cs**

在旧构造函数下方添加：

```csharp
private readonly OpcUaStrategyOptions? _options;

/// <summary>
/// DI 构造函数：ILogger + Machine + OPC UA Options。
/// </summary>
public OpcUaStrategy(
    ILogger? logger,
    Machine machine,
    OpcUaStrategyOptions options) : base(logger, machine)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
}
```

- [ ] **Step 3: 修改 ScannerStrategy.cs**

在旧构造函数下方添加：

```csharp
private readonly ScannerStrategyOptions? _options;

/// <summary>
/// DI 构造函数：ILogger + Machine + Scanner Options。
/// </summary>
public ScannerStrategy(
    ILogger? logger,
    Machine machine,
    ScannerStrategyOptions options) : base(logger, machine)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
}
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: 运行全部测试**

Run: `dotnet test`
Expected: All tests passed

- [ ] **Step 6: Commit**

```bash
git add src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs \
        src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs \
        src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs
git commit -m "feat: Fins、OpcUa、Scanner Strategy 添加 ILogger 可选构造函数"
```

---

### Task 2.5: Machine 基类和子类添加 ILogger 构造函数

**Files:**
- Modify: `src/CollectionDrivers.Common/Machine.cs`
- Modify: `src/CollectionDrivers.BatteryDriver/BatteryMachine.cs`
- Modify: `src/CollectionDrivers.FinsDriver/FinsMachine.cs`
- Modify: `src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs`
- Modify: `src/CollectionDrivers.ScannerDriver/ScannerMachine.cs`

**Interfaces:**
- Produces: `Machine(ILogger?)` 新增, 子类转发构造函数

- [ ] **Step 1: 修改 Machine.cs**

在现有构造函数下方添加：

```csharp
/// <summary>
/// DI 构造函数：仅注入 Logger。配置通过后续 Initialize(MachineOptions) 设置。
/// </summary>
protected Machine(ILogger? logger)
{
    Logger = logger ?? LoggingFactory.CreateLogger(typeof(Machine).FullName);
    // Configuration 和 Id 等通过 Initialize 或旧路径设置
}
```

- [ ] **Step 2: 修改 BatteryMachine.cs**

```csharp
public class BatteryMachine : Machine
{
    public BatteryMachine(Machines machines, object configuration) : base(machines, configuration) { }

    /// <summary>DI 构造函数</summary>
    public BatteryMachine(ILogger? logger) : base(logger) { }
}
```

- [ ] **Step 3: 同模式修改 FinsMachine、OpcUaMachine、ScannerMachine**

```csharp
// FinsMachine
public FinsMachine(ILogger? logger) : base(logger) { }

// OpcUaMachine
public OpcUaMachine(ILogger? logger) : base(logger) { }

// ScannerMachine
public ScannerMachine(ILogger? logger) : base(logger) { }
```

- [ ] **Step 4: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 5: 运行全部测试**

Run: `dotnet test`
Expected: All tests passed

- [ ] **Step 6: Commit**

```bash
git add src/CollectionDrivers.Common/Machine.cs \
        src/CollectionDrivers.BatteryDriver/BatteryMachine.cs \
        src/CollectionDrivers.FinsDriver/FinsMachine.cs \
        src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs \
        src/CollectionDrivers.ScannerDriver/ScannerMachine.cs
git commit -m "feat: Machine 基类和子类添加 ILogger 可选构造函数"
```

---

### Phase 2 检查点

- [ ] `dotnet build` 全部编译通过
- [ ] `dotnet test` 全部测试通过（旧 new BatteryTcpStrategy(machine) 仍可用）
- [ ] 新 `(ILogger?, Machine, *Options)` 构造函数可正常构造
- [ ] Phase 2 PR 可合并

---

## Phase 3：Machine 接口化 + Options 替代 dynamic（破坏性）

> TDD 策略：每个接口实现/配置迁移先写验证测试→实现→存量测试通过。

### Task 3.1: Handler 实现 IHandler 接口

**Files:**
- Modify: `src/CollectionDrivers.Common/Handler.cs`

**Interfaces:**
- Produces: `Handler : IHandler`

- [ ] **Step 1: 修改 Handler.cs**

```csharp
public class Handler : IHandler
{
    // ... 现有代码不变 ...
}
```

编译即通过——`Handler` 已有 `OnStrategySweepCompleteInternalAsync()` 方法，签名与 `IHandler` 匹配。

- [ ] **Step 2: 编译验证并确认**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 3: Commit**

```bash
git add src/CollectionDrivers.Common/Handler.cs
git commit -m "feat: Handler 显式实现 IHandler 接口"
```

---

### Task 3.2: Machine 移除 abstract 并实现 IMachineContext

**Files:**
- Modify: `src/CollectionDrivers.Common/Machine.cs`

**Interfaces:**
- Consumes: `IMachineContext`, `IHandler`
- Produces: `Machine : IMachineContext`（非 abstract）

- [ ] **Step 1: 写失败测试**

在 `tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs` 追加：

```csharp
[Fact]
public void Machine_ImplementsIMachineContext()
{
    var machine = new Machine((ILogger?)null);
    var ctx = machine as IMachineContext;

    Assert.NotNull(ctx);
    Assert.NotNull(ctx.Id);
}
```

Run: `dotnet test tests/battery-driver.test --filter "Machine_ImplementsIMachineContext"`
Expected: FAIL — `Machine` 未实现 `IMachineContext` 或是 abstract

- [ ] **Step 2: 修改 Machine.cs**

关键变更：
1. 移除 `abstract` 关键字
2. 添加 `IMachineContext` 接口实现
3. 添加私有字段存储状态
4. 添加 `Initialize(MachineOptions)` 方法
5. Id 属性改为 fallback 逻辑

```csharp
public class Machine : IMachineContext, IAsyncDisposable
{
    protected readonly ILogger Logger;

    private string? _id;
    private bool _enabled;
    private int _sweepMs;
    private Strategy? _strategy;
    private Handler? _handler;
    private List<Transport> _transports = new();

    // ── 旧构造函数 ──
    protected Machine(Machines machines, object configuration)
    {
        Configuration = configuration;
        Enabled = Configuration.machine.enabled;
        Logger = LoggingFactory.CreateLogger(typeof(Machine).FullName);
        Logger.LogDebug($"[{Id}] Creating machine, enabled: {Enabled}");
    }

    // ── 新构造函数 ──
    protected Machine(ILogger? logger)
    {
        Logger = logger ?? LoggingFactory.CreateLogger(typeof(Machine).FullName);
    }

    public dynamic? Configuration { get; }

    // ── IMachineContext 实现 ──

    public string Id => _id ?? Configuration?.machine?.id ?? "";

    public bool Enabled
    {
        get => _enabled || (Configuration?.machine?.enabled ?? false);
        private set => _enabled = value;
    }

    public int SweepMs => _sweepMs > 0 ? _sweepMs : (Configuration?.type?["sweep_ms"] ?? 5000);

    public IHandler Handler
    {
        get
        {
            if (_handler == null) throw new InvalidOperationException("Handler not set");
            return _handler;
        }
    }

    public IReadOnlyList<Transport> Transports => _transports;

    public bool StrategySuccess => _strategy?.LastSuccess ?? false;

    public bool StrategyHealthy => _strategy?.IsHealthy ?? false;

    /// <summary>用 MachineOptions 初始化状态</summary>
    public void Initialize(MachineOptions options)
    {
        _sweepMs = options.SweepMs;
        _enabled = options.Enabled;
        _id = options.Id;
    }

    /// <summary>回挂 Strategy 实例</summary>
    internal void SetStrategy(Strategy strategy) => _strategy = strategy;

    /// <summary>回挂 Handler 实例</summary>
    internal void SetHandler(Handler handler) => _handler = handler;

    /// <summary>回挂 Transport 列表</summary>
    internal void SetTransports(List<Transport> transports) => _transports = transports;

    // ── Strategy/Handler/Transport 属性保持公开（向后兼容） ──

    public Strategy Strategy
    {
        get => _strategy ?? throw new InvalidOperationException("Strategy not set");
        private set => _strategy = value;
    }

    public Handler HandlerInstance => _handler ?? throw new InvalidOperationException("Handler not set");

    // Transport 属性移除 private set（通过 SetTransports 设置）

    public async Task Stop()
    {
        // 基类默认无操作，子类可重写
        await Task.CompletedTask;
    }

    // DisposeAsync 保持现有实现不变
}
```

- [ ] **Step 3: 运行测试确认**

Run: `dotnet test tests/battery-driver.test --filter "Machine_ImplementsIMachineContext"`
Expected: 1 test passed

Run: `dotnet test`
Expected: All tests passed (向后兼容路径保留)

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/Machine.cs \
        tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs
git commit -m "feat: Machine 移除 abstract 并实现 IMachineContext 接口"
```

---

### Task 3.3: Strategy 基类和子类改为使用 Options

**Files:**
- Modify: `src/CollectionDrivers.Common/Strategy.cs`
- Modify: `src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs`

**Interfaces:**
- Consumes: `IMachineContext`
- Produces: Phase 3 构造函数使用 `IMachineContext` 替代 `Machine`，子类使用注入的 Options

- [ ] **Step 1: 修改 Strategy.cs 构造函数（IMachineContext 版本）**

```csharp
// 标记旧构造函数 Obsolete
[Obsolete("Use Strategy(ILogger?, IMachineContext) instead")]
protected Strategy(Machine machine) : this(null, machine) { }

// Phase 3 新主构造函数
protected Strategy(ILogger? logger, IMachineContext context)
{
    Logger = logger ?? LoggingFactory.CreateLogger(GetType().FullName);
    Context = context;
    SweepMs = context.SweepMs;
}

/// <summary>所属机器上下文（Phase 3+ 使用此属性替代 Machine）</summary>
public IMachineContext Context { get; }

/// <summary>所属设备实例（[Obsolete] Phase 3+ 使用 Context）</summary>
[Obsolete("Use Context instead")]
public Machine Machine => (Context as Machine)!;
```

- [ ] **Step 2: 修改 BatteryTcpStrategy.cs**

关键变更：`_options` 字段移入新构造函数，`InitializeAsync` 改用 Options：

```csharp
public class BatteryTcpStrategy : Strategy, IDisposable
{
    // ... 现有字段 ...

    private readonly BatteryTcpStrategyOptions? _options;

    [Obsolete]
    public BatteryTcpStrategy(Machine machine) : base(machine) { }

    /// <summary>DI 构造函数（Phase 3）</summary>
    public BatteryTcpStrategy(
        ILogger? logger,
        IMachineContext context,
        BatteryTcpStrategyOptions options) : base(logger, context)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override async Task InitializeAsync()
    {
        // 优先使用 _options，fallback 到 Machine.Configuration.strategy (dynamic)
        int port = _options?.Port
            ?? (Context is Machine m ? Convert.ToInt32(m.Configuration?.strategy?["port"]) : 13000);
        int warningPort = _options?.WarningPort
            ?? (Context is Machine m ? Convert.ToInt32(m.Configuration?.strategy?["warning_port"]) : 13100);
        int heartbeatTimeout = _options?.HeartbeatTimeoutS
            ?? (Context is Machine m ? Convert.ToInt32(m.Configuration?.strategy?["heartbeat_timeout_s"]) : 60);

        // ... 其余 InitializeAsync 逻辑不变，仅 port/warningPort/heartbeatTimeout 来源变化 ...
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);
        _connection?.CheckHeartbeat();
        LastSuccess = _connection?.IsConnected ?? false;
        IsHealthy = _connection?.IsConnected ?? false;
        if (Context?.Handler != null)  // Machine. → Context.
            await Context.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: 运行全部测试**

Run: `dotnet test`
Expected: All tests passed

- [ ] **Step 5: Commit**

```bash
git add src/CollectionDrivers.Common/Strategy.cs \
        src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs
git commit -m "feat: Strategy 基类添加 IMachineContext 构造函数，BatteryTcpStrategy 改用 Options"
```

---

### Task 3.4: FinsStrategy、OpcUaStrategy、ScannerStrategy 改用 Options 和 IMachineContext

**Files:**
- Modify: `src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs`
- Modify: `src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs`
- Modify: `src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs`

**Interfaces:**
- Produces: 三个子类中 `Machine.` → `Context.` 引用替换

- [ ] **Step 1: 修改 FinsStrategy.cs**

1. `Machine?.Handler` → `Context?.Handler`
2. 配置读取优先 `_options`，fallback `Machine.Configuration.strategy`
3. `Machine.Configuration.strategy` → 先检查 `_options` 再 fallback

```csharp
// FinsStrategy 构造函数
public FinsStrategy(
    ILogger? logger,
    IMachineContext context,
    FinsStrategyOptions options) : base(logger, context)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
    // 若 _options 有值且非默认，优先使用；否则从 dynamic 读取
    if (_options.RemoteIp != "192.168.1.1" && _options.Port != 9600)
    {
        _config = new FinsConfig
        {
            RemoteIp = _options.RemoteIp,
            Port = _options.Port,
            TimeoutMs = _options.TimeoutMs,
            Collectors = _options.Collectors
        };
    }
    else
    {
        var rawConfig = (Context as Machine)?.Configuration?.strategy;
        _config = ParseConfig(rawConfig);
    }
}
```

`SweepAsync` 中所有 `Machine?.Handler.` → `Context?.Handler.`

- [ ] **Step 2: 修改 OpcUaStrategy.cs**

同样模式：`_options` 优先，fallback 到 `Machine.Configuration.strategy`。
`SweepAsync` 中 `Machine?.Handler` → `Context?.Handler`。

配置解析从 `ParseConfig(rawConfig)` 变为优先使用 `_options.Collectors` 等已绑定数据。

- [ ] **Step 3: 修改 ScannerStrategy.cs**

同样模式。

```csharp
// ScannerStrategy 构造函数
public ScannerStrategy(
    ILogger? logger,
    IMachineContext context,
    ScannerStrategyOptions options) : base(logger, context)
{
    _options = options ?? throw new ArgumentNullException(nameof(options));
    // Connection 配置优先从 _options 读取
    _config = new ScannerConfig { Name = context.Id };
    if (!string.IsNullOrEmpty(_options.Host))
    {
        _config.Host = _options.Host;
        _config.Port = _options.Port;
        _config.Mode = _options.Mode;
        _config.RetryCount = _options.RetryCount;
        _config.ConnectTimeoutMs = _options.ConnectTimeoutMs;
        _config.ReceiveTimeoutMs = _options.ReceiveTimeoutMs;
        _config.DedupEnabled = _options.DedupEnabled;
        _config.Protocol = new ScannerProtocolConfig
        {
            SendCommandHex = _options.Protocol.SendCommandHex,
            ResponseEncoding = _options.Protocol.ResponseEncoding,
            BarcodeRegex = _options.Protocol.BarcodeRegex,
            RegexGroupIndex = _options.Protocol.RegexGroupIndex,
            FrameDelimiterHex = _options.Protocol.FrameDelimiterHex,
            RemovePrefixes = _options.Protocol.RemovePrefixes,
            RemoveSuffixes = _options.Protocol.RemoveSuffixes
        };
    }
    else
    {
        var rawConfig = (Context as Machine)?.Configuration?.strategy;
        _config = ParseConfig(rawConfig);
    }
    // _parser, _command, _connection 初始化保持不变
}
```

`SweepAsync` 中 `Machine?.Handler` → `Context?.Handler`

- [ ] **Step 4: 编译验证并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs \
        src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs \
        src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs
git commit -m "feat: Fins/OpcUa/Scanner Strategy 改用 Options 优先配置和 IMachineContext"
```

---

### Task 3.5: TransportHandler 和 InfluxDbTransport 改用 IMachineContext 和 Options

**Files:**
- Modify: `src/CollectionDrivers.Common/TransportHandler.cs`
- Modify: `src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs`

- [ ] **Step 1: 修改 TransportHandler.cs**

```csharp
public override async Task OnStrategySweepCompleteInternalAsync()
{
    var transports = Context.Transports;  // Machine. → Context.
    if (transports.Count == 0) return;

    var payload = new SweepEndPayload(
        Observation: new SweepEndObservation(
            Time: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Machine: Context.Id,        // Machine. → Context.
            Name: "sweep"
        ),
        Online: Context.StrategySuccess,   // Machine. → Context.
        Healthy: Context.StrategyHealthy   // Machine. → Context.
    );

    foreach (var transport in transports)
    {
        try
        {
            await transport.SendAsync("SWEEP_END", payload);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{MachineId}] Transport {TransportType} SWEEP_END send failed",
                Context.Id, transport.GetType().Name);  // Machine. → Context.
        }
    }
}
```

- [ ] **Step 2: 修改 InfluxDbTransport.cs**

`CreateAsync` 优先从 `_options` 读取配置：

```csharp
public override async Task CreateAsync()
{
    if (_options != null && !string.IsNullOrEmpty(_options.Host))
    {
        _bucket = _options.Bucket;
        _org = _options.Org;
        var host = _options.Host;
        var token = _options.Token;
        _client = InfluxDBClientFactory.Create(host, token);
        _writeApi = _client.GetWriteApiAsync();
        _transformLookup = _options.Transformers;
    }
    else
    {
        // fallback: 旧 dynamic 路径（Machine.Configuration.transport）
        var transportCfg = (Context as Machine)?.Configuration?.transport;
        if (transportCfg == null) return;
        // ... 现有旧逻辑 ...
    }
}
```

- [ ] **Step 3: 编译验证并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/TransportHandler.cs \
        src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs
git commit -m "feat: TransportHandler 和 InfluxDbTransport 改用 IMachineContext 和 Options"
```

---

### Task 3.6: LoggingFactory 标记 Obsolete

**Files:**
- Modify: `src/CollectionDrivers.Common/LoggingFactory.cs`

- [ ] **Step 1: 标记 Obsolete**

```csharp
[Obsolete("Phase 5 将删除。DI 化后用构造函数注入 ILogger<T> 替代。")]
public static class LoggingFactory
{
    // 现有实现不变
}
```

- [ ] **Step 2: 编译验证（会有 Obsolete 警告，但应编译通过）**

Run: `dotnet build`
Expected: Build succeeded with CS0618 warnings

- [ ] **Step 3: Commit**

```bash
git add src/CollectionDrivers.Common/LoggingFactory.cs
git commit -m "feat: LoggingFactory 标记 Obsolete，引导迁移到 DI Logger"
```

---

### Phase 3 检查点

- [ ] `dotnet build` 编译通过（允许 Obsolete 警告）
- [ ] `dotnet test` 全部测试通过
- [ ] `Machine` 实现 `IMachineContext`，`Handler` 实现 `IHandler`
- [ ] 所有 Strategy/Handler/Transport 内部引用从 `Machine.` 迁移到 `Context.`
- [ ] Options 优先读取，fallback 到 dynamic（向后兼容）
- [ ] Phase 3 PR 可合并

---

## Phase 4：DI 容器接管创建（破坏性）

> TDD 策略：MachineScope 为核心新代码→先写集成测试→实现→旧测试仍通过。

### Task 4.1: 实现 MachineScopeFactory

**Files:**
- Create: `src/CollectionDrivers.Common/MachineScopeFactory.cs`
- Modify: `src/CollectionDrivers.Common/ServiceCollectionExtensions.cs`

**Interfaces:**
- Produces: `MachineScopeFactory : IMachineScopeFactory`

- [ ] **Step 1: 创建 MachineScopeFactory.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// IMachineScopeFactory 的生产实现。注入 DI 基础设施并为每台机器创建独立 Scope。
/// </summary>
internal class MachineScopeFactory : IMachineScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DriverTypeRegistry _registry;
    private readonly ILogger<MachineScopeFactory> _logger;

    public MachineScopeFactory(
        IServiceScopeFactory scopeFactory,
        IEnumerable<DriverTypeRegistry.Entry> entries,
        ILogger<MachineScopeFactory> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _registry = new DriverTypeRegistry();
        foreach (var e in entries)
        {
            // 检测重复 DriverId
            if (_registry.Entries.Any(x => x.DriverId == e.DriverId))
                _logger.LogWarning("Duplicate DriverId '{DriverId}' registered, first-wins", e.DriverId);
            else
                _registry.Entries.Add(e);
        }
        _logger.LogInformation("DriverTypeRegistry initialized with {Count} entries", _registry.Entries.Count);
    }

    public IMachineScope CreateScope(MachineOptions config)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            return new MachineScope(scope, config, _registry);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
```

- [ ] **Step 2: 修改 ServiceCollectionExtensions.cs**

将占位实现替换为生产实现：

```csharp
// 将
services.AddSingleton<IMachineScopeFactory, MachineScopeFactoryPlaceholder>();
// 改为
services.AddSingleton<IMachineScopeFactory, MachineScopeFactory>();
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/MachineScopeFactory.cs \
        src/CollectionDrivers.Common/ServiceCollectionExtensions.cs
git commit -m "feat: 实现 MachineScopeFactory 生产版本"
```

---

### Task 4.2: 实现 MachineScope

**Files:**
- Create: `src/CollectionDrivers.Common/MachineScope.cs`

**Interfaces:**
- Consumes: `IServiceScope`, `MachineOptions`, `DriverTypeRegistry`
- Produces: `MachineScope : IMachineScope`

- [ ] **Step 1: 写集成测试**

创建 `tests/battery-driver.test/scopes/MachineScopeTest.cs`：

```csharp
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using CollectionDrivers.Common;
using CollectionDrivers.Transport.InfluxDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace battery_driver.test.Scopes;

/// <summary>
/// MachineScope 集成测试。
/// </summary>
public class MachineScopeTest
{
    [Fact]
    public async Task MachineScope_CreatesAndDisposes_Successfully()
    {
        // ── Arrange ──
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Machines:0:Id"] = "scope-test",
                ["Machines:0:Enabled"] = "true",
                ["Machines:0:DriverId"] = "Battery",
                ["Machines:0:SweepMs"] = "50",
                ["Machines:0:Strategy:Port"] = "13000",
            }).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(config);
        services.Configure<CollectionDriverOptions>(
            config.GetSection("CollectionDrivers"));
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            "Battery", typeof(Machine), typeof(BatteryTcpStrategy),
            typeof(TransportHandler), typeof(InfluxDbTransport),
            typeof(BatteryTcpStrategyOptions), typeof(InfluxDbTransportOptions)));

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var registry = new DriverTypeRegistry();
        registry.Entries.Add(new DriverTypeRegistry.Entry(
            "Battery", typeof(Machine), typeof(BatteryTcpStrategy),
            typeof(TransportHandler), typeof(InfluxDbTransport),
            typeof(BatteryTcpStrategyOptions), typeof(InfluxDbTransportOptions)));

        var machineCfg = sp.GetRequiredService<IOptions<CollectionDriverOptions>>()
            .Value.Machines[0];
        machineCfg.Configuration = config.GetSection("CollectionDrivers:Machines:0");

        // ── Act ──
        var scope = scopeFactory.CreateScope();
        var machineScope = new MachineScope(scope, machineCfg, registry);

        // ── Assert ──
        Assert.NotNull(machineScope.Context);
        Assert.Equal("scope-test", machineScope.Context.Id);

        // ── Cleanup ──
        await machineScope.DisposeAsync();
    }

    [Fact]
    public async Task MachineScope_RunAsync_StopsOnCancellation()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Machines:0:Id"] = "run-test",
                ["Machines:0:Enabled"] = "true",
                ["Machines:0:DriverId"] = "Battery",
                ["Machines:0:SweepMs"] = "100",
                ["Machines:0:Strategy:Port"] = "13000",
            }).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.Configure<CollectionDriverOptions>(
            config.GetSection("CollectionDrivers"));
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            "Battery", typeof(Machine), typeof(BatteryTcpStrategy),
            typeof(TransportHandler), typeof(InfluxDbTransport),
            typeof(BatteryTcpStrategyOptions), typeof(InfluxDbTransportOptions)));

        var sp = services.BuildServiceProvider();
        var registry = new DriverTypeRegistry();
        registry.Entries.Add(new DriverTypeRegistry.Entry(
            "Battery", typeof(Machine), typeof(BatteryTcpStrategy),
            typeof(TransportHandler), typeof(InfluxDbTransport),
            typeof(BatteryTcpStrategyOptions), typeof(InfluxDbTransportOptions)));

        var machineCfg = sp.GetRequiredService<IOptions<CollectionDriverOptions>>()
            .Value.Machines[0];
        machineCfg.Configuration = config.GetSection("CollectionDrivers:Machines:0");

        var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var machineScope = new MachineScope(scope, machineCfg, registry);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await machineScope.RunAsync(cts.Token);

        // 不抛异常即可——证明 CreateAsync 和 InitializeAsync 执行完毕
        await machineScope.DisposeAsync();
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/battery-driver.test --filter "MachineScope_"`
Expected: FAIL — `MachineScope` 类不存在

- [ ] **Step 3: 创建 MachineScope.cs**（完整实现，见 Spec 第 4.3 节）

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的采集 Scope。封装 IServiceScope + 组件生命周期。
/// </summary>
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

        // Step 1: 查找类型注册
        var entry = registry.Find(config.DriverId)
                    ?? throw new InvalidOperationException(
                        $"No driver registered for DriverId '{config.DriverId ?? "(null)"}'");

        // Step 2: 绑定 Options
        var strategyOptions = BindOptions(
            config.Configuration?.GetSection("Strategy"), entry.StrategyOptionsType);
        var transportOptions = BindOptions(
            config.Configuration?.GetSection("Transport"), entry.TransportOptionsType);

        // Step 3: 构造 Machine
        var machineLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.MachineType));
        var machine = (Machine)ActivatorUtilities.CreateInstance(
            sp, entry.MachineType, machineLogger);
        machine.Initialize(config);
        Context = machine;

        // Step 4: 构造 Strategy
        var strategyLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.StrategyType));
        _strategy = (Strategy)ActivatorUtilities.CreateInstance(sp,
            entry.StrategyType, strategyLogger, Context, strategyOptions);
        _strategy.OnError += (ex, ctx) =>
            ((ILogger)strategyLogger).LogError(
                ex, "[{Id}] Strategy error in {Context}", Context.Id, ctx);

        // Step 5: 构造 Handler
        var handlerLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.HandlerType));
        _handler = (Handler)ActivatorUtilities.CreateInstance(sp,
            entry.HandlerType, handlerLogger, Context);

        // Step 6: 构造 Transport
        var transportLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.TransportType));
        var transport = (Transport)ActivatorUtilities.CreateInstance(sp,
            entry.TransportType, transportLogger, Context, transportOptions);
        var transports = new List<Transport> { transport };

        // Step 7: 回挂到 Machine
        machine.SetStrategy(_strategy);
        machine.SetHandler(_handler);
        machine.SetTransports(transports);
    }

    public async Task RunAsync(CancellationToken ct)
    {
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

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/battery-driver.test --filter "MachineScope_"`
Expected: Tests pass (MachineScope 构造和运行正常)

- [ ] **Step 5: Commit**

```bash
git add src/CollectionDrivers.Common/MachineScope.cs \
        tests/battery-driver.test/scopes/MachineScopeTest.cs
git commit -m "feat: 实现 MachineScope 核心编排逻辑"
```

---

### Task 4.3: 重构 DriverHostService

**Files:**
- Modify: `src/CollectionDrivers.Common/DriverHostService.cs`

**Interfaces:**
- Consumes: `IConfiguration`, `IOptions<CollectionDriverOptions>`, `IMachineScopeFactory`
- Produces: 薄编排层 DriverHostService

- [ ] **Step 1: 重写 DriverHostService.cs**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动宿主服务。通过 IConfiguration 接收宿主配置，
/// 使用 IMachineScopeFactory 为每台机器创建独立 Scope 并启动采集循环。
/// </summary>
public class DriverHostService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IOptions<CollectionDriverOptions> _options;
    private readonly IMachineScopeFactory _scopeFactory;
    private readonly ILogger<DriverHostService> _logger;

    /// <summary>构造函数注入</summary>
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

    /// <inheritdoc/>
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

            // 填充 IConfiguration 引用
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

- [ ] **Step 2: 保留旧 DriverHostService 代码路径**

在 `DriverHostService` 中添加配置开关，允许旧路径和新路径并存：

```csharp
// 构造函数中检测：若 IMachineScopeFactory 为占位实现则走旧路径
private readonly bool _useNewPath;

public DriverHostService(...)
{
    // ...
    _useNewPath = scopeFactory is not MachineScopeFactoryPlaceholder;
}

protected override async Task ExecuteAsync(CancellationToken ct)
{
    if (_useNewPath)
    {
        await ExecuteNewAsync(ct);
    }
    else
    {
        await ExecuteOldAsync(ct);
    }
}
```

> Phase 4 实现 `MachineScopeFactory` 后 `_useNewPath` 自动为 true。
> Phase 5 删除旧代码路径。

- [ ] **Step 3: 编译验证并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/DriverHostService.cs
git commit -m "feat: 重构 DriverHostService 为薄编排层（新 DI 路径与旧路径并存）"
```

---

### Task 4.4: 标记旧路径 Obsolete

**Files:**
- Modify: `src/CollectionDrivers.Common/Machine.cs` (AddStrategyAsync/AddHandlerAsync/AddTransportAsync)
- Modify: `src/CollectionDrivers.Common/Machines.cs`

- [ ] **Step 1: Machine.cs 反射方法标记 Obsolete**

```csharp
[Obsolete("Phase 5 将删除。使用 MachineScope + ActivatorUtilities 替代。")]
public async Task<Machine> AddHandlerAsync(Type type) { /* 现有实现 */ }

[Obsolete("Phase 5 将删除。")]
public async Task<Machine> AddStrategyAsync(Type type) { /* 现有实现 */ }

[Obsolete("Phase 5 将删除。")]
public async Task<Machine> AddTransportAsync(Type type) { /* 现有实现 */ }
```

- [ ] **Step 2: Machines.cs 类标记 Obsolete**

```csharp
[Obsolete("Phase 5 将删除。使用 IMachineScopeFactory 替代。")]
public class Machines { /* 现有实现 */ }
```

- [ ] **Step 3: 编译验证（允许 Obsolete 警告）**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/CollectionDrivers.Common/Machine.cs \
        src/CollectionDrivers.Common/Machines.cs
git commit -m "feat: 标记旧反射创建路径和 Machines 类为 Obsolete"
```

---

### Phase 4 检查点

- [ ] `dotnet build` 编译通过
- [ ] `dotnet test` 全部测试通过
- [ ] `MachineScopeFactory` + `MachineScope` 集成测试通过
- [ ] `DriverHostService` 新路径可运行
- [ ] Phase 4 PR 可合并

---

## Phase 5：清理（清理性）

> TDD 跳过理由：Phase 5 为纯删除操作，无新增业务逻辑

### Task 5.1: 删除 LoggingFactory

**Files:**
- Delete: `src/CollectionDrivers.Common/LoggingFactory.cs`
- Modify: `src/CollectionDrivers.Common/Strategy.cs` (移除 fallback)
- Modify: `src/CollectionDrivers.Common/Handler.cs` (移除 fallback)
- Modify: `src/CollectionDrivers.Common/Transport.cs` (移除 fallback)
- Modify: `src/CollectionDrivers.Common/Machine.cs` (移除 fallback)

- [ ] **Step 1: 修改所有 Logger 初始化代码**

将 `logger ?? LoggingFactory.CreateLogger(...)` 改为 `logger!`（DI 生产路径保证非 null）

```csharp
// Strategy
protected Strategy(ILogger? logger, IMachineContext context)
{
    Logger = logger!;  // DI 路径保证非 null
    Context = context;
    SweepMs = context.SweepMs;
}

// Handler、Transport、Machine 同理
```

- [ ] **Step 2: 删除 LoggingFactory.cs**

```bash
rm src/CollectionDrivers.Common/LoggingFactory.cs
```

- [ ] **Step 3: 编译并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git rm src/CollectionDrivers.Common/LoggingFactory.cs
git add src/CollectionDrivers.Common/Strategy.cs \
        src/CollectionDrivers.Common/Handler.cs \
        src/CollectionDrivers.Common/Transport.cs \
        src/CollectionDrivers.Common/Machine.cs
git commit -m "refactor: 删除 LoggingFactory，DI Logger 完全接管"
```

---

### Task 5.2: 删除旧构造函数和空 Machine 子类

**Files:**
- Modify: `src/CollectionDrivers.Common/Strategy.cs` (删除 [Obsolete] 构造函数)
- Modify: `src/CollectionDrivers.Common/Handler.cs` (删除 [Obsolete] 构造函数)
- Modify: `src/CollectionDrivers.Common/Transport.cs` (删除 [Obsolete] 构造函数)
- Delete: `src/CollectionDrivers.BatteryDriver/BatteryMachine.cs`
- Delete: `src/CollectionDrivers.FinsDriver/FinsMachine.cs`
- Delete: `src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs`
- Delete: `src/CollectionDrivers.ScannerDriver/ScannerMachine.cs`
- Modify: 各 Strategy/Transport 子类 (删除 [Obsolete] 构造函数)

- [ ] **Step 1: 删除所有旧构造函数和空子类**

从 `Strategy.cs`、`Handler.cs`、`Transport.cs` 删除 `[Obsolete]` 构造函数。
从各子类删除 `[Obsolete]` 旧构造函数。
删除 4 个空的 `*Machine.cs` 子类文件。

- [ ] **Step 2: 更新 DriverTypeRegistry entries**

将 `MachineType: typeof(Machine)` 保持不变（各 Add*Driver 中已经是 `typeof(Machine)`）。

- [ ] **Step 3: 编译并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git rm src/CollectionDrivers.BatteryDriver/BatteryMachine.cs \
       src/CollectionDrivers.FinsDriver/FinsMachine.cs \
       src/CollectionDrivers.OpcUaDriver/OpcUaMachine.cs \
       src/CollectionDrivers.ScannerDriver/ScannerMachine.cs
git add src/CollectionDrivers.Common/Strategy.cs \
        src/CollectionDrivers.Common/Handler.cs \
        src/CollectionDrivers.Common/Transport.cs \
        src/CollectionDrivers.BatteryDriver/strategies/BatteryTcpStrategy.cs \
        src/CollectionDrivers.FinsDriver/strategies/FinsStrategy.cs \
        src/CollectionDrivers.OpcUaDriver/strategies/OpcUaStrategy.cs \
        src/CollectionDrivers.ScannerDriver/strategies/ScannerStrategy.cs \
        src/CollectionDrivers.Transport.InfluxDB/InfluxDbTransport.cs
git commit -m "refactor: 删除旧构造函数和空 Machine 子类"
```

---

### Task 5.3: 删除 Machines 类和 dynamic 配置路径

**Files:**
- Delete: `src/CollectionDrivers.Common/Machines.cs`
- Modify: `src/CollectionDrivers.Common/Machine.cs` (删除 `dynamic Configuration` 属性、`Machines` 构造函数、反射方法)
- Modify: `src/CollectionDrivers.Common/DriverHostService.cs` (删除旧路径)
- Modify: `src/CollectionDrivers.Common/CollectionDrivers.Common.csproj` (移除 YamlDotNet)

- [ ] **Step 1: 清理 Machine.cs**

- 删除 `protected Machine(Machines machines, object configuration)` 旧构造函数
- 删除 `public dynamic? Configuration { get; }` 属性
- 删除 `AddStrategyAsync`、`AddHandlerAsync`、`AddTransportAsync` 方法
- `Id` 简化为 `public string Id => _id!;`
- `Enabled` 简化为纯字段访问
- `SweepMs` 简化为 `public int SweepMs => _sweepMs;`

- [ ] **Step 2: 删除 Machines.cs**

```bash
rm src/CollectionDrivers.Common/Machines.cs
```

- [ ] **Step 3: 删除 DriverHostService 旧路径**

移除 `_useNewPath` 开关和 `ExecuteOldAsync` 方法，仅保留新路径。

- [ ] **Step 4: 移除 YamlDotNet 引用**

从 `CollectionDrivers.Common.csproj` 移除：
```xml
<PackageReference Include="YamlDotNet" Version="16.3.0" />
```

- [ ] **Step 5: 更新测试（删除依赖旧构造函数的测试）**

更新测试文件，所有 `new BatteryMachine(machines, config)` 改为使用 `MachineScope` 集成测试或直接使用新构造函数。

- [ ] **Step 6: 编译并运行测试**

Run: `dotnet build && dotnet test`
Expected: All pass

- [ ] **Step 7: Commit**

```bash
git rm src/CollectionDrivers.Common/Machines.cs
git add src/CollectionDrivers.Common/Machine.cs \
        src/CollectionDrivers.Common/DriverHostService.cs \
        src/CollectionDrivers.Common/CollectionDrivers.Common.csproj \
        tests/
git commit -m "refactor: 删除 Machines 类、dynamic 配置路径和 YamlDotNet 依赖"
```

---

### Phase 5 检查点

- [ ] `dotnet build` 编译通过，无 Obsolete 警告
- [ ] `dotnet test` 全部测试通过
- [ ] `LoggingFactory` 已删除
- [ ] 所有反射 `Activator.CreateInstance` 代码已删除
- [ ] 所有空 `*Machine` 子类已删除
- [ ] `dynamic` 配置路径已完全消除
- [ ] 代码净减少
- [ ] Phase 5 PR 可合并

---

## 测试清单

| Phase | 新增测试 | 存量测试要求 |
|-------|---------|-------------|
| 1 | `OptionsBindingTest` (3 tests) | 全部通过 |
| 2 | `Constructor_WithILoggerAndMachine` | 全部通过 |
| 3 | `Machine_ImplementsIMachineContext` | 全部通过 |
| 4 | `MachineScopeTest` (2 tests) | 全部通过 |
| 5 | 更新存量测试 | 全部通过（部分测试重写） |

---

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| Phase 4 运行时异常 | MachineScope 集成测试 + try-catch 故障隔离 |
| 多驱动同类型机器配置串扰 | 每机器独立 IConfiguration 段绑定 |
| 旧宿主使用 YAML→dynamic 路径中断 | Phase 1-4 并存，Phase 5 才删除，给宿主迁移窗口 |
| 测试大面积重写 | Phase 2-4 保持旧构造函数可用，测试逐步迁移 |
