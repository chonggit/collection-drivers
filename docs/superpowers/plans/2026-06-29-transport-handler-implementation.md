# TransportHandler 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建 TransportHandler 类桥接 Handler → Transport 数据流，使所有 Driver 在 SweepAsync 结束时自动推送 SWEEP_END 到配置的 Transport。

**Architecture:** 在 CollectionDrivers.Common 中新建 TransportHandler 类，继承 base.Handler，override OnStrategySweepCompleteAsync（构建 payload）和 AfterSweepCompleteAsync（调 Transport.SendAsync），不改动 base-driver 或任何 Strategy。

**Tech Stack:** .NET 8, C#, l99.driver.@base

**TDD 跳过声明:** TransportHandler 为纯委托类（payload 构建 + null 检查 + try-catch 兜底），无领域逻辑，属于"脚手架/委托"类别，跳过 TDD。

---

### Task 1: 创建 TransportHandler 类

**Files:**
- Create: `src/CollectionDrivers.Common/TransportHandler.cs`

- [ ] **Step 1: 写入 TransportHandler.cs**

```csharp
using l99.driver.@base;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 传输层处理器。在每次采集周期结束时，将设备状态
/// 通过 Machine.Transport 推送到外部系统（如 InfluxDB、MQTT）。
/// 与 fanuc 的 FanucOne 模式一致：Handler override → Transport.SendAsync。
/// </summary>
public class TransportHandler : Handler
{
    public TransportHandler(Machine machine) : base(machine)
    {
    }

    /// <summary>
    /// 构建 SWEEP_END payload，包含设备的 online/healthy 状态。
    /// 返回 null 时 AfterSweepCompleteAsync 会跳过发送。
    /// </summary>
    protected override async Task<dynamic?> OnStrategySweepCompleteAsync(
        Machine machine, dynamic? beforeSweepComplete)
    {
        return new
        {
            observation = new
            {
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                machine = machine.Id,
                name = "sweep"
            },
            state = new
            {
                data = new
                {
                    online = machine.StrategySuccess,
                    healthy = machine.StrategyHealthy
                }
            }
        };
    }

    /// <summary>
    /// 将 SWEEP_END payload 发送到 Transport。
    /// Transport 为 null（创建失败）或 SendAsync 异常时安全降级，不中断采集循环。
    /// </summary>
    protected override async Task AfterSweepCompleteAsync(
        Machine machine, dynamic? onSweepComplete)
    {
        if (onSweepComplete == null) return;
        if (machine.Transport == null) return;

        try
        {
            await machine.Transport.SendAsync("SWEEP_END", null, onSweepComplete);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{MachineId}] Transport SWEEP_END send failed", machine.Id);
        }
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build src/CollectionDrivers.Common/CollectionDrivers.Common.csproj
```

预期: Build succeeded, 0 Error(s)

- [ ] **Step 3: 提交**

```bash
git add src/CollectionDrivers.Common/TransportHandler.cs
git commit -m "feat: 添加 TransportHandler 桥接 Handler 到 Transport 的数据通路"
```

---

### Task 2: 更新 YAML 示例文件

**Files:**
- Modify: `examples/config.scanner.yml:6`
- Modify: `examples/config.fins.yml:6`
- Modify: `examples/config.opcua.yml:6`
- Modify: `examples/config.system.yml:5`
- Modify: `examples/config.transport.influxdb.yml:6`

- [ ] **Step 1: 更新 config.scanner.yml 第 6 行**

```yaml
# 将
handler: l99.driver.@base.Handler, base-driver
# 改为
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

- [ ] **Step 2: 更新 config.fins.yml 第 6 行**

```yaml
# 将
handler: l99.driver.@base.Handler, base-driver
# 改为
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

- [ ] **Step 3: 更新 config.opcua.yml 第 6 行**

```yaml
# 将
handler: l99.driver.@base.Handler, base-driver
# 改为
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

- [ ] **Step 4: 更新 config.system.yml 第 5 行**

```yaml
# 将
handler: CollectionDrivers.BatteryDriver.BatteryHandler, CollectionDrivers.BatteryDriver
# 改为
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

- [ ] **Step 5: 更新 config.transport.influxdb.yml 第 6 行**

```yaml
# 将
handler: l99.driver.@base.Handler, base-driver
# 改为
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

- [ ] **Step 6: 编译全解决方案**

```bash
dotnet build
```

预期: Build succeeded, 0 Error(s)

- [ ] **Step 7: 运行全部测试**

```bash
dotnet test
```

预期: All tests passed (42 tests across 4 projects)

- [ ] **Step 8: 提交**

```bash
git add examples/config.scanner.yml examples/config.fins.yml examples/config.opcua.yml examples/config.system.yml examples/config.transport.influxdb.yml
git commit -m "feat: 所有 YAML 示例统一使用 TransportHandler"
```

---

### Task 3: 标记 BatteryHandler 为 [Obsolete]

**Files:**
- Modify: `src/CollectionDrivers.BatteryDriver/BatteryHandler.cs`

- [ ] **Step 1: 添加 [Obsolete] 注释**

```csharp
using l99.driver.@base;

namespace CollectionDrivers.BatteryDriver;

/// <summary>
/// 已废弃。功能已迁移至 <see cref="Common.TransportHandler"/>。
/// 保留此类避免已有的 YAML 引用在过渡期报错。
/// </summary>
[Obsolete("Use CollectionDrivers.Common.TransportHandler instead")]
public class BatteryHandler : Handler
{
    public BatteryHandler(Machine machine) : base(machine)
    {
    }
}
```

- [ ] **Step 2: 编译验证**

```bash
dotnet build src/CollectionDrivers.BatteryDriver/CollectionDrivers.BatteryDriver.csproj
```

预期: Build succeeded（可能有 CS0618 警告，可忽略）

- [ ] **Step 3: 提交**

```bash
git add src/CollectionDrivers.BatteryDriver/BatteryHandler.cs
git commit -m "feat: 标记 BatteryHandler 为 Obsolete，功能已迁移至 TransportHandler"
```
