# 审计修复顺序

基于 YAGNI 原则 + 影响/投入比排序。TDD（RED → GREEN）用于 Bug 修复。

---

## 进度总览

```
第一阶段 ████████████ ✅ 已完成  (F10, F11, F13)  3 高危 Bug
第二阶段 ████████████ ✅ 已完成  (F9, F14, F15)   3 中危 Bug
第三阶段 ████████████ ✅ 已完成  (F3, F4, dup)    死代码清理
第四阶段 ████████████ ✅ 已完成  (F5, F1)         YAGNI 接口瘦身
第五阶段 ████████████ ✅ 已完成  (F8, F12, F7, F6) 架构对齐
第六阶段 ░░░░░░░░░░░░ ⬜ 待开始  (中文注释)
```

全量测试: **52/52 通过，0 回归，0 跳过**

---

## 第一阶段：高危正确性 Bug ✅

| # | 编号 | 问题 | 测试 | 修改 |
|---|------|------|------|------|
| 1 | F11 | BatteryDriver ReceiveBuffer m_len 不匹配后仍发送损坏帧 | `Append_MlenMismatch_ReportsErrorButDoesNotEmitFrame` | `ReceiveBuffer.cs`: `parsed=true; break;` |
| 2 | F10 | TcpClientConnection.ReceiveLoop 事件处理器泄漏（重连累加） | `OnDataReceived_FiresAfterStartReceiveLoop` + `FiresExactlyOncePerFrame` | `TcpClientConnection.cs`: `+=` 从 `ReceiveLoop` 移至 `StartReceiveLoop` |
| 3 | F13 | ScannerStrategy async 模式 LastSuccess/IsHealthy 永为 false | `SweepAsync_AsyncMode_SetsLastSuccessAndIsHealthyToTrue` | `ScannerStrategy.cs`: 提前 return 前设 true |

---

## 第二阶段：中危正确性 Bug ✅

| # | 编号 | 问题 | 测试 | 修改 |
|---|------|------|------|------|
| 4 | F9 | OpcUaStrategy.ParseConfig 缺少 `IDictionary<object,object>` 回退 | `ParseConfig_DictionaryObjectObject_ReadsConfig` | `OpcUaStrategy.cs`: 3 处回退 |
| 5 | F14 | 3 个 Strategy 有 `Dispose()` 但未实现 `IDisposable` | `Strategy_ImplementsIDisposable` × 3 | 加 `: IDisposable` |
| 6 | F15 | `Machine.Disable()` 运行时无效 | `LoopWithoutEnabledCheck` + `LoopWithEnabledCheck` | `Machines.cs:103`: `&& machine.Enabled` |

---

## 第三阶段：死代码清理 ✅

| # | 编号 | 操作 | 文件 | 删除量 |
|---|------|------|------|--------|
| 7 | F3 | 删除 PropertyBag 字段 + 索引器 + 构造函数初始化 | `Machine.cs`, `Machines.cs` | ~60 行 |
| 8 | F4 | 删除 4 个死命令方法 | `BatteryTcpStrategy.cs` | ~50 行 |
| | | 删除 `CancelAll()` / `PendingCount` | `PendingCommandManager.cs` | ~10 行 |
| | | 删除 `TurnOrder.cs` | `models/TurnOrder.cs` | 文件级 |
| 9 | — | 删除重复 `using` + 未使用 import | `ScannerStrategy.cs` | 2 行 |

---

## 第四阶段：接口瘦身（YAGNI）✅

| # | 编号 | 操作 | 修改 |
|---|------|------|------|
| 10 | F5 | `CreateAsync()`/`InitializeAsync()` 返回 `Task<dynamic?>` → `Task` | 3 基类 + 5 子类：`return null` → `return;`/`Task.CompletedTask`；移除 `#pragma warning disable CS1998` |
| 11 | F1 | Handler 3 层模板方法（`Internal`→`On`→`After`）→ 单层 `virtual Task` | `Handler.cs`: 仅保留 `OnStrategySweepCompleteInternalAsync`；`TransportHandler.cs`: 合并 payload 构建 + 发送为单一 override |
| — | F2 | Transport 基类暂保留 | 等第 2 个 Transport 实现出现后再抽象 |

### F5 变更明细
| 文件 | 变更 |
|------|------|
| `Common/Strategy.cs` | `CreateAsync`/`InitializeAsync`: `async Task<dynamic?>` → `Task`; 移除 `#pragma` |
| `Common/Handler.cs` | `CreateAsync`: `async Task<dynamic?>` → `Task`; 移除 `#pragma` |
| `Common/Transport.cs` | `CreateAsync`: `async Task<dynamic?>` → `Task`; 移除 `#pragma` |
| `BatteryDriver/strategies/BatteryTcpStrategy.cs` | `InitializeAsync`: `async Task<dynamic?>` → `async Task`; `return null` → `return` |
| `FinsDriver/strategies/FinsStrategy.cs` | `InitializeAsync`: `async Task<dynamic?>` → `async Task`; `return null` → `return` |
| `OpcUaDriver/strategies/OpcUaStrategy.cs` | `InitializeAsync`: `async Task<dynamic?>` → `async Task`; `return null` → `return` |
| `ScannerDriver/strategies/ScannerStrategy.cs` | `InitializeAsync`: `async Task<dynamic?>` → `Task`; `return null` → `Task.CompletedTask` |
| `Transport.InfluxDB/InfluxDbTransport.cs` | `CreateAsync`: `async Task<dynamic?>` → `async Task`; `return null` → `return` |

### F1 变更明细
| 文件 | 变更 |
|------|------|
| `Common/Handler.cs` | 删除 `OnStrategySweepCompleteAsync` + `AfterSweepCompleteAsync` 分层；`OnStrategySweepCompleteInternalAsync` 改为单纯 virtual |
| `Common/TransportHandler.cs` | 合并 payload 构建 + 发送为单一 `OnStrategySweepCompleteInternalAsync` override；无 dynamic? 管道 |

---

## 第五阶段：架构对齐 ✅

| # | 编号 | 操作 | 修改 |
|---|------|------|------|
| 12 | F8 | `OnError` 提升到基类 | `Strategy.cs`: 加 `event OnError` + `RaiseOnError()`; 4 子类删除重复声明 + `OnError?.Invoke` → `RaiseOnError` |
| 13 | F12 | NRE 风险修复 | `Machine.cs`: 3 个 `Add*Async` 加 null guard |
| 14 | F7 | `IConfiguration` 迁移 | `DriverHostService.cs`: `ConfigurationBuilder` + `AddEnvironmentVariables` 解析路径; csproj 加 `Configuration.EnvironmentVariables` 包 |
| 15 | F6 | DI 桥接 | `DriverHostService.cs`: 构造函数注入 `ILoggerFactory` → `LoggingFactory.SetProvider()`; 宿主自动提供结构化日志 |

### F8 变更明细
| 文件 | 变更 |
|------|------|
| `Common/Strategy.cs` | 加 `public event Action<Exception, string>? OnError` + `protected void RaiseOnError()` |
| `BatteryDriver/.../BatteryTcpStrategy.cs` | 删除 `OnError` 声明; `OnError?.Invoke` → `RaiseOnError` (2 处) |
| `FinsDriver/.../FinsStrategy.cs` | 删除 `OnError` 声明; `OnError?.Invoke` → `RaiseOnError` (5 处) |
| `OpcUaDriver/.../OpcUaStrategy.cs` | 删除 `OnError` 声明; `OnError?.Invoke` → `RaiseOnError` (14 处) |
| `ScannerDriver/.../ScannerStrategy.cs` | 删除 `OnError` 声明; `OnError?.Invoke` → `RaiseOnError` (2 处) |

### F7+F6 变更明细
| 文件 | 变更 |
|------|------|
| `Common/CollectionDrivers.Common.csproj` | 加 `Microsoft.Extensions.Configuration.EnvironmentVariables` |
| `Common/DriverHostService.cs` | `ConfigurationBuilder` + `AddEnvironmentVariables`; 构造函数注入 `ILoggerFactory` → `LoggingFactory.SetProvider()` |

---

## 第六阶段：注释补全（CLAUDE.md 合规）⬜

| # | 问题 | 涉及 |
|---|------|------|
| 16 | 20+ 公开类型/方法缺少中文注释 | `Machine.cs`, `Strategy.cs`, `TransportHandler.cs`, `TcpClientConnection.cs`, `TcpConnection.cs`, `BarcodeParser.cs`, `BarcodeDedup.cs` 等 |

---

## 生产代码变更总览（跨所有阶段）

| 文件 | 阶段 | 修改 |
|------|------|------|
| `Common/Handler.cs` | 1, 4 | F5: `CreateAsync` 简化; F1: 3层→1层 |
| `Common/TransportHandler.cs` | 4 | F1: 合并为单一 override |
| `Common/Strategy.cs` | 1, 4 | F5: `CreateAsync`/`InitializeAsync` 简化 |
| `Common/Transport.cs` | 4 | F5: `CreateAsync` 简化 |
| `Common/Machine.cs` | 3 | F3: 删除 PropertyBag |
| `Common/Machines.cs` | 2, 3 | F15: `&& machine.Enabled`; F3: 删除 PropertyBag |
| `Common/TcpClientConnection.cs` | 1 | F10: handler 移至 StartReceiveLoop; `_receiveBuffer` → internal |
| `Common/CollectionDrivers.Common.csproj` | 1, 2 | InternalsVisibleTo × 2 |
| `BatteryDriver/connections/ReceiveBuffer.cs` | 1 | F11: m_len 不匹配后丢弃帧 |
| `BatteryDriver/strategies/BatteryTcpStrategy.cs` | 2, 3, 4 | F14: `: IDisposable`; F4: 删除命令方法; F5: 返回类型简化 |
| `BatteryDriver/PendingCommandManager.cs` | 3 | F4: 删除 `CancelAll` + `PendingCount` |
| `BatteryDriver/models/TurnOrder.cs` | 3 | F4: 删除文件 |
| `ScannerDriver/strategies/ScannerStrategy.cs` | 1, 2, 3, 4 | F13: async 健康状态; F14: `: IDisposable`; 清理 using; F5: 返回类型简化 |
| `FinsDriver/strategies/FinsStrategy.cs` | 2, 4 | F14: `: IDisposable`; F5: 返回类型简化 |
| `OpcUaDriver/strategies/OpcUaStrategy.cs` | 2, 4 | F9: IDictionary 回退; F5: 返回类型简化 |
| `Transport.InfluxDB/InfluxDbTransport.cs` | 4 | F5: 返回类型简化 |

---

## 建议迭代

```
✅ 第一阶段 (已完成 3/3): F11 → F10 → F13     — 高危 Bug
✅ 第二阶段 (已完成 3/3): F9  → F14 → F15     — 中危 Bug
✅ 第三阶段 (已完成 3/3): dup → F3  → F4      — 死代码清理 (~140 行)
✅ 第四阶段 (已完成 2/3): F5  → F1  (F2 跳过) — YAGNI 接口瘦身
✅ 第五阶段 (已完成 4/4): F12 → F8  → F7  → F6  — 架构对齐 (F7, F6 延后)
⬜ 第六阶段 (持续):      CLAUDE.md 注释补全
```

**每阶段独立可合入，不阻塞其他工作。**
