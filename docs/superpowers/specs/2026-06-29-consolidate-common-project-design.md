# 合并 base-driver 到 CollectionDrivers.Common

## 背景

当前 `base-driver` 是 fanuc 遗留项目，含大量未使用代码（Veneers/Veneer 管道、Bootstrap 启动器、Handler 中 DataArrival/DataChange/Error 链）。由于未正式被其他系统使用，无需考虑兼容性。

## 目标

1. 删除 `src/base-driver/` 项目
2. 将有用的类迁入 `CollectionDrivers.Common`，命名空间改为 `CollectionDrivers.Common`
3. 清理所有无引用代码
4. 更新所有 .csproj / .slnx / using 语句

## 迁入 Common 的类（共 6 个）

### Machine.cs — 精简后迁入

保留：构造器（含 `LoggingFactory`、`_propertyBag`）、属性包（`_propertyBag` + `this[string]`）、`Id`、`Enabled`、`Info`、`Configuration`、`ToString`、`Disable`、`Stop`、Handler 相关（`Handler` + `AddHandlerAsync`）、Strategy 相关（`Strategy` + `StrategySuccess` + `StrategyHealthy` + `AddStrategyAsync` + `InitStrategyAsync` + `RunStrategyAsync`）、Transport 相关（`Transport` + `AddTransportAsync`）。

删除（共 10 个成员 + 构造器内 1 行 + AddHandlerAsync 内 3 行）：

- 构造器第 20 行：`Veneers = new Veneers(this);` — 删除
- `AddHandlerAsync` 第 92–94 行：`Veneers.OnDataArrivalAsync = ...` / `Veneers.OnDataChangeAsync = ...` / `Veneers.OnErrorAsync = ...` — 删除这三行
- `Veneers` 属性、`VeneersApplied` 属性
- `ApplyVeneer`、`SliceVeneer`(×2)、`ApplyVeneerAcrossSlices`(×2)、`PeelVeneerAsync`、`PeelAcrossVeneerAsync`、`MarkVeneer`

### Handler.cs — 精简后迁入

保留：`Logger`、`Machine`、构造器、`CreateAsync`、`OnStrategySweepCompleteInternalAsync` + `BeforeSweepCompleteAsync` + `OnStrategySweepCompleteAsync` + `AfterSweepCompleteAsync`。

删除（13 个方法）：
- `OnDataArrivalInternalAsync`、`BeforeDataArrivalAsync`、`OnDataArrivalAsync`、`AfterDataArrivalAsync`
- `OnDataChangeInternalAsync`、`BeforeDataChangeAsync`、`OnDataChangeAsync`、`AfterDataChangeAsync`
- `OnErrorInternalAsync`、`BeforeDataErrorAsync`、`OnErrorAsync`、`AfterDataErrorAsync`
- `OnGenerateIntermediateModelAsync`

### Transport.cs — 精简后迁入

删除：`OnGenerateIntermediateModelAsync`。保留：构造器、`Machine`、`Logger`、`CreateAsync`、`ConnectAsync`、`SendAsync`。

### Strategy.cs — 不动迁入

全部成员在用，直接迁入，不改内容。

### Machines.cs — 精简后迁入

删除：
- 第 111 行 `var assemblyName = typeof(Machines).Assembly.GetName().Name;`（移除后 variable 无引用）
- 第 125–134 行默认值中的 `l99.driver.@base.*` 命名空间 — `type`/`strategy`/`handler`/`transport` 改为无默认值，即 YAML 中没有对应字段时 `AddHandlerAsync` 等创建流程会收到 null Type 并进入 catch 块记录错误日志，不会崩溃
- 第 138 行 TODO 注释 `// TODO: 'collectors' is not base impl, move to Fanuc`
- 第 155 行 `collectors = new Dictionary<string, object>()`
- 第 158 行 TODO 注释 `// TODO: not base impl, move to Fanuc`
- 第 160–163 行 `if (builtConfig.strategy != null) foreach (var collectorType in ...)`
- 第 166 行日志语句中 `JObject.FromObject(builtConfig).ToString()` 改为 `JObject.FromObject(builtConfig)`（移除 .ToString() 保持风格一致）

### LoggingFactory.cs — 不动迁入

全部成员在用，直接迁入。

## 迁移文件的修改方式

所有 6 个文件从 `src/base-driver/base/` 复制到 `src/CollectionDrivers.Common/`，然后修改 namespace 并做上述精简。迁移后删除 `src/base-driver/` 目录。不是"新建文件"，是"复制→修改→删除原目录"。

## 删除的文件（3 个）

| 文件 | 理由 |
|---|---|
| `base-driver/base/Bootstrap.cs` | 被 DriverHostService 取代，无引用 |
| `base-driver/base/Veneer.cs` | 0 个 Strategy 使用 |
| `base-driver/base/Veneers.cs` | 0 个 Strategy 使用 |

## InfluxDbTransport.cs 同步清理

- 删除 `HandleDataArriveAsync(Veneer veneer, dynamic data)` — 从未被触发
- 删除 `HasTransform(Veneer veneer)` — 同上
- `SendAsync` 删除 `var veneer = (Veneer)parameters[1];` 和 DATA_ARRIVE 分支，简化为仅处理 SWEEP_END（`parameters[2]` 即为 payload，不变）和 INT_MODEL（不变）
- 类声明第 14 行 `l99.driver.@base.Transport` → `Transport`（同命名空间后无需全限定）

## 命名空间变更

恢复成 `l99.driver.@base` → 改为 `using CollectionDrivers.Common`（如类不需要该命名空间则直接删除 using）。

**src/ 下 12 个文件需要改 using：**

| 文件 | 操作 |
|---|---|
| `Common/DriverHostService.cs` | `using l99.driver.@base` → `using CollectionDrivers.Common` |
| `Common/TransportHandler.cs` | 删除 `using l99.driver.@base`（同命名空间） |
| `BatteryDriver/BatteryHandler.cs` | 改为 `using CollectionDrivers.Common` |
| `BatteryDriver/BatteryMachine.cs` | 改为 `using CollectionDrivers.Common` |
| `BatteryDriver/strategies/BatteryTcpStrategy.cs` | 改为 `using CollectionDrivers.Common` |
| `FinsDriver/FinsMachine.cs` | 改为 `using CollectionDrivers.Common` |
| `FinsDriver/strategies/FinsStrategy.cs` | 改为 `using CollectionDrivers.Common` |
| `OpcUaDriver/OpcUaMachine.cs` | 改为 `using CollectionDrivers.Common` |
| `OpcUaDriver/strategies/OpcUaStrategy.cs` | 改为 `using CollectionDrivers.Common` |
| `ScannerDriver/ScannerMachine.cs` | 改为 `using CollectionDrivers.Common` |
| `ScannerDriver/strategies/ScannerStrategy.cs` | 改为 `using CollectionDrivers.Common` |
| `Transport.InfluxDB/InfluxDbTransport.cs` | `using l99.driver.@base` → `using CollectionDrivers.Common`；类声明 `l99.driver.@base.Transport` → `Transport` |

**tests/ 下 1 个文件需要改 using：**

| 文件 | 操作 |
|---|---|
| `battery-driver.test/strategies/BatteryTcpStrategyTest.cs` | `using l99.driver.@base` → `using CollectionDrivers.Common` |

## csproj 引用变更

| 项目 | 操作 |
|---|---|
| `CollectionDrivers.Common.csproj` | 删除 `base-driver` ProjectReference（第 10 行）；新增 `Newtonsoft.Json` PackageReference（第 13.0.4 版，与 base-driver.csproj 一致） |
| `BatteryDriver.csproj` | `base-driver` → `CollectionDrivers.Common` |
| `FinsDriver.csproj` | `base-driver` → `CollectionDrivers.Common` |
| `OpcUaDriver.csproj` | `base-driver` → `CollectionDrivers.Common` |
| `ScannerDriver.csproj` | 删除 `base-driver` 引用（第 5 行）—— `CollectionDrivers.Common` 已存在（第 4 行），无需新增 |
| `Transport.InfluxDB.csproj` | `base-driver` → `CollectionDrivers.Common` |

## 解决方案变更

- `collection-drivers.slnx`：删除 `<Project Path="src/base-driver/base-driver.csproj" />`
- 删除 `src/base-driver/` 整个目录

## 测试项目间接依赖

测试项目 .csproj 不直接引用 base-driver，它们引用被测试的 driver 项目，driver 项目引用 Common。迁移后依赖链：
- `battery-driver.test → BatteryDriver → Common` ✅
- `fins-driver.test → FinsDriver → Common` ✅
- `opcua-driver.test → OpcUaDriver → Common` ✅
- `scanner-driver.test → ScannerDriver → Common` ✅

无循环引用风险。

## 实现文件总览

- 迁移（复制→修改）：6 个文件（Machine/Handler/Transport/Strategy/Machines/LoggingFactory）
- 删除源：`src/base-driver/` 整个目录
- src/ using 变更：12 个文件
- tests/ using 变更：1 个文件
- csproj 变更：6 个项目
- slnx 变更：1 个文件
- InfluxDbTransport.cs：同步清理 Veneer 依赖
