# 审计修复顺序

基于 YAGNI 原则 + 影响/投入比排序。遵循 TDD（RED → GREEN → REFACTOR）。

---

## 进度总览

```
第一阶段 ████████████ ✅ 已完成  (F10, F11, F13)
第二阶段 ████████████ ✅ 已完成  (F9, F14, F15)
第三阶段 ░░░░░░░░░░░░ ⬜ 待开始  (F3, F4, F1-dup)
第四阶段 ░░░░░░░░░░░░ ⬜ 待开始  (F1, F2, F5)
第五阶段 ░░░░░░░░░░░░ ⬜ 待开始  (F6, F7, F8, F12)
第六阶段 ░░░░░░░░░░░░ ⬜ 待开始  (中文注释)
```

全量测试: **52/52 通过，0 回归，0 跳过**

---

## 第一阶段：高危正确性 Bug ✅

| # | 编号 | 问题 | 测试 | 修改 |
|---|------|------|------|------|
| 1 | F11 | BatteryDriver ReceiveBuffer m_len 不匹配后仍发送损坏帧 | `Append_MlenMismatch_ReportsErrorButDoesNotEmitFrame` | `ReceiveBuffer.cs`: m_len 不匹配后 `parsed=true; break;` 丢弃帧 |
| 2 | F10 | TcpClientConnection.ReceiveLoop 事件处理器泄漏（重连累加） | `OnDataReceived_FiresAfterStartReceiveLoop` + `FiresExactlyOncePerFrame` | `TcpClientConnection.cs`: `+=` 从 `ReceiveLoop` 移至 `StartReceiveLoop` |
| 3 | F13 | ScannerStrategy async 模式 LastSuccess/IsHealthy 永为 false | `SweepAsync_AsyncMode_SetsLastSuccessAndIsHealthyToTrue` | `ScannerStrategy.cs`: async 模式提前 return 前设 `LastSuccess=true; IsHealthy=true` |

---

## 第二阶段：中危正确性 Bug ✅

| # | 编号 | 问题 | 测试 | 修改 |
|---|------|------|------|------|
| 4 | F9 | OpcUaStrategy.ParseConfig 缺少 `IDictionary<object,object>` 回退 | `ParseConfig_DictionaryObjectObject_ReadsConfig` | `OpcUaStrategy.cs`: 外层 config + 内层 collector + node 共 3 处加回退 |
| 5 | F14 | 3 个 Strategy 有 `Dispose()` 但未实现 `IDisposable` | `Strategy_ImplementsIDisposable` × 3 | `ScannerStrategy.cs` / `BatteryTcpStrategy.cs` / `FinsStrategy.cs`: 加 `: IDisposable` |
| 6 | F15 | `Machine.Disable()` 运行时无效 | `LoopWithoutEnabledCheck_KeepsRunningAfterDisable` + `LoopWithEnabledCheck_StopsAfterDisable` | `Machines.cs:103`: while 条件加 `&& machine.Enabled` |

---

## 第三阶段：死代码清理（低风险高收益）⬜

| # | 编号 | 问题 | 涉及文件 | 删除量 |
|---|------|------|----------|--------|
| 7 | F3 | PropertyBag 死代码（Machine + Machines 各一份） | `Machine.cs` / `Machines.cs` | ~50 行 |
| 8 | F4 | 3 个命令方法 + `CancelAll()`/`PendingCount` 零调用方 | `BatteryTcpStrategy.cs` / `PendingCommandManager.cs` | ~100 行 |
| 9 | — | `ScannerStrategy.cs` 重复 `using CollectionDrivers.Common` + 无用 import | `ScannerStrategy.cs:1,3` | 2 行 |

---

## 第四阶段：接口瘦身（YAGNI）⬜

| # | 编号 | 问题 | 方案 |
|---|------|------|------|
| 10 | F5 | `CreateAsync()`/`InitializeAsync()` 返回 `Task<dynamic?>` 始终 null | 改为 `Task`；`OnStrategySweepCompleteAsync` 保留（唯一有效用例） |
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

### 生产代码（第一阶段）
| 文件 | 修改 |
|------|------|
| `src/.../BatteryDriver/connections/ReceiveBuffer.cs` | F11: m_len 不匹配后丢弃帧 |
| `src/.../Common/TcpClientConnection.cs` | F10: handler 注册移至 StartReceiveLoop; `_receiveBuffer` → internal |
| `src/.../ScannerDriver/strategies/ScannerStrategy.cs` | F13: async 模式设置健康状态 |

### 生产代码（第二阶段）
| 文件 | 修改 |
|------|------|
| `src/.../OpcUaDriver/strategies/OpcUaStrategy.cs` | F9: 3 处 IDictionary<object,object> 回退 |
| `src/.../ScannerDriver/strategies/ScannerStrategy.cs` | F14: `: Strategy, IDisposable` |
| `src/.../BatteryDriver/strategies/BatteryTcpStrategy.cs` | F14: `: Strategy, IDisposable` |
| `src/.../FinsDriver/strategies/FinsStrategy.cs` | F14: `: Strategy, IDisposable` |
| `src/.../Common/Machines.cs` | F15: `&& machine.Enabled` |
| `src/.../Common/CollectionDrivers.Common.csproj` | InternalsVisibleTo: battery-driver.test, scanner-driver.test |

### 测试代码（全部 6 个新增文件/测试）
| 文件 | 新增测试 |
|------|----------|
| `tests/battery-driver.test/connections/ReceiveBufferTest.cs` | +1: `Append_MlenMismatch_ReportsErrorButDoesNotEmitFrame` |
| `tests/battery-driver.test/connections/TcpClientConnectionTest.cs` | 新文件: +2: `OnDataReceived_FiresAfterStartReceiveLoop` + `FiresExactlyOncePerFrame` |
| `tests/battery-driver.test/strategies/BatteryTcpStrategyTest.cs` | +3: `Strategy_ImplementsIDisposable` + `LoopWithoutEnabledCheck` + `LoopWithEnabledCheck` |
| `tests/scanner-driver.test/strategies/ScannerStrategyTest.cs` | +2: `SweepAsync_AsyncMode_SetsLastSuccessAndIsHealthyToTrue` + `Strategy_ImplementsIDisposable` |
| `tests/fins-driver.test/strategies/FinsStrategyTest.cs` | +1: `Strategy_ImplementsIDisposable` |
| `tests/opcua-driver.test/strategies/OpcUaStrategyTest.cs` | +1: `ParseConfig_DictionaryObjectObject_ReadsConfig` |

---

## 建议迭代

```
✅ 第一阶段 (已完成): F11 → F10 → F13     — 高危正确性 Bug
✅ 第二阶段 (已完成): F9  → F14 → F15     — 中危正确性 Bug
⬜ 第三阶段 (建议 1-2h): F3  → F4  → dup   — 死代码清理
⬜ 第四阶段 (建议 2-4h): F5  → F1  → F2    — YAGNI 接口瘦身
⬜ 第五阶段 (按需):     F8  → F7  → F6 → F12 — 架构对齐
⬜ 第六阶段 (持续):     CLAUDE.md 注释补全
```

**每阶段独立可合入，不阻塞其他工作。**
