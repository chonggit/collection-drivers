# 审计修复顺序

基于 YAGNI 原则 + 影响/投入比排序。TDD（RED → GREEN）用于 Bug 修复。

---

## 进度总览

```
第一阶段 ████████████ ✅ 已完成  (F10, F11, F13)  3 高危 Bug
第二阶段 ████████████ ✅ 已完成  (F9, F14, F15)   3 中危 Bug
第三阶段 ████████████ ✅ 已完成  (F3, F4, dup)    死代码清理
第四阶段 ░░░░░░░░░░░░ ⬜ 待开始  (F1, F2, F5)    YAGNI 接口瘦身
第五阶段 ░░░░░░░░░░░░ ⬜ 待开始  (F6, F7, F8, F12) 架构对齐
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
| 7 | F3 | 删除 PropertyBag 字段 + 索引器 + 构造函数初始化 | `Machine.cs` | ~30 行 |
| | | 删除 PropertyBag 字段 + 索引器 + 构造函数初始化 | `Machines.cs` | ~30 行 |
| 8 | F4 | 删除 `StartFormationAsync` / `PauseFormationAsync` / `ResumeFormationAsync` / `SendCommandAsync` | `BatteryTcpStrategy.cs` | ~50 行 |
| | | 删除 `CancelAll()` / `PendingCount` | `PendingCommandManager.cs` | ~10 行 |
| | | 删除 `TurnOrder.cs`（StartFormationAsync 唯一引用方被删） | `models/TurnOrder.cs` | 文件级 |
| 9 | — | 删除重复 `using CollectionDrivers.Common` + 未使用 `System.Text.RegularExpressions` | `ScannerStrategy.cs` | 2 行 |

**合计: ~140 行死代码删除，1 个文件移除。**

---

## 第四阶段：接口瘦身（YAGNI）⬜

| # | 编号 | 问题 | 方案 |
|---|------|------|------|
| 10 | F5 | `CreateAsync()`/`InitializeAsync()` 返回 `Task<dynamic?>` 始终 null | 改为 `Task`；`OnStrategySweepCompleteAsync` 保留 dynamic? |
| 11 | F1 | Handler 3 层模板方法仅 1 个子类（TransportHandler） | 合并或简化模板方法链 |
| 12 | F2 | Transport 基类仅 1 个实现（InfluxDbTransport） | 合并或等第 2 个实现出现后再抽象 |

---

## 第五阶段：架构对齐（按需排期）⬜

| # | 编号 | 问题 | 方案 |
|---|------|------|------|
| 13 | F8 | `OnError` 事件在 4 个 Strategy 子类中各自声明 | 提升到基类 `Strategy` |
| 14 | F7 | DriverHostService 绕过 `IConfiguration` | 引入 `Microsoft.Extensions.Configuration.Yaml` |
| 15 | F6 | LoggingFactory 静态单例 | 引入标准 .NET DI |
| 16 | F12 | `AddTransportAsync` 等 NRE 风险 | `type.FullName` 移入 try 块内 |

---

## 第六阶段：注释补全（CLAUDE.md 合规）⬜

| # | 问题 | 涉及 |
|---|------|------|
| 17 | 20+ 公开类型/方法缺少中文注释 | `Machine.cs`, `Strategy.cs`, `Handler.cs`, `TcpClientConnection.cs`, `TcpConnection.cs`, `BarcodeParser.cs`, `BarcodeDedup.cs` 等 |

---

## 修改文件索引

### 生产代码
| 文件 | 阶段 | 修改 |
|------|------|------|
| `Common/Machines.cs` | 2, 3 | F15: `&& machine.Enabled`; F3: 删除 PropertyBag |
| `Common/Machine.cs` | 3 | F3: 删除 PropertyBag |
| `Common/TcpClientConnection.cs` | 1 | F10: handler 移至 StartReceiveLoop; `_receiveBuffer` → internal |
| `Common/CollectionDrivers.Common.csproj` | 1, 2 | InternalsVisibleTo × 2 |
| `BatteryDriver/connections/ReceiveBuffer.cs` | 1 | F11: m_len 不匹配后丢弃帧 |
| `BatteryDriver/strategies/BatteryTcpStrategy.cs` | 2, 3 | F14: `: IDisposable`; F4: 删除 4 个命令方法 |
| `BatteryDriver/PendingCommandManager.cs` | 3 | F4: 删除 `CancelAll` + `PendingCount` |
| `BatteryDriver/models/TurnOrder.cs` | 3 | F4: 删除文件 |
| `ScannerDriver/strategies/ScannerStrategy.cs` | 1, 2, 3 | F13: async 健康状态; F14: `: IDisposable`; 清理重复 using |
| `FinsDriver/strategies/FinsStrategy.cs` | 2 | F14: `: IDisposable` |
| `OpcUaDriver/strategies/OpcUaStrategy.cs` | 2 | F9: 3 处 IDictionary<object,object> 回退 |

### 测试代码
| 文件 | 新增测试 |
|------|----------|
| `tests/battery-driver.test/connections/ReceiveBufferTest.cs` | +1: `Append_MlenMismatch_ReportsErrorButDoesNotEmitFrame` |
| `tests/battery-driver.test/connections/TcpClientConnectionTest.cs` | 🆕 新文件: +2 |
| `tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs` | +3: `Strategy_ImplementsIDisposable` + `LoopWithoutEnabledCheck` + `LoopWithEnabledCheck` |
| `tests/scanner-driver.test/strategies/ScannerStrategyTest.cs` | +2: `SweepAsync_AsyncMode_` + `Strategy_ImplementsIDisposable` |
| `tests/fins-driver.test/strategies/FinsStrategyTest.cs` | +1: `Strategy_ImplementsIDisposable` |
| `tests/opcua-driver.test/strategies/OpcUaStrategyTest.cs` | +1: `ParseConfig_DictionaryObjectObject_ReadsConfig` |

---

## 建议迭代

```
✅ 第一阶段 (已完成 3/3): F11 → F10 → F13     — 高危 Bug
✅ 第二阶段 (已完成 3/3): F9  → F14 → F15     — 中危 Bug
✅ 第三阶段 (已完成 3/3): dup → F3  → F4      — 死代码清理
⬜ 第四阶段 (建议 2-4h): F5  → F1  → F2       — YAGNI 接口瘦身
⬜ 第五阶段 (按需):      F8  → F7  → F6 → F12 — 架构对齐
⬜ 第六阶段 (持续):      CLAUDE.md 注释补全
```

**每阶段独立可合入，不阻塞其他工作。**
