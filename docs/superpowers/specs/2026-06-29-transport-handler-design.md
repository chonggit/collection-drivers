# TransportHandler — Handler 到 Transport 数据通路连接

## 背景

当前所有 CollectionDrivers 的 SweepAsync 末尾都会调用 `Handler.OnStrategySweepCompleteInternalAsync()`，但基类 Handler 的 `AfterSweepCompleteAsync` 是空方法，没有调用 `Machine.Transport.SendAsync("SWEEP_END", ...)`。导致即使配置了 Transport（如 InfluxDB），也无法收到任何数据。

数据流向本应是：

```
Strategy 采集/转换 → Handler 处理 → Transport 发送
```

实际现状：

```
Strategy 采集/转换 → Handler（基类空实现）→ Transport 永不触发
```

## 方案

在 `CollectionDrivers.Common` 中新建 `TransportHandler` 类，继承 `l99.driver.@base.Handler`，override 采集周期结束阶段的三个方法完成 Transport 推送。

### 与 fanuc 一致的分层

```
fanuc:   FanucExtendedStrategy → FanucOne(Handler) → Transport
本方案:  BatteryStrategy → TransportHandler → Transport
```

两个驱动共用同一个 base-driver 的 Machine + Transport 基类，Handler 层各自实现，互不影响。

## TransportHandler 实现

```csharp
namespace CollectionDrivers.Common;

public class TransportHandler : Handler
{
    public TransportHandler(Machine machine) : base(machine) { }

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

### SWEEP_END payload 结构

```
{
  observation: {
    time:     long   // Unix 毫秒时间戳
    machine:  string // Machine.Id
    name:     "sweep"
  },
  state: {
    data: {
      online:  bool  // machine.StrategySuccess
      healthy: bool  // machine.StrategyHealthy
    }
  }
}
```

与 `InfluxDbTransport.HandleSweepEndAsync` 消费端对齐。该方法的 `template.Render(new { data.observation, data.state.data })` 将 payload 映射为 Scriban 模型：`observation.time`、`observation.machine`、`data.online`、`data.healthy`。

### 安全保护

| 场景 | 行为 |
|---|---|
| Transport 创建失败（为 null） | 跳过发送，不抛异常 |
| SendAsync 抛出异常（网络超时等） | try-catch 兜底，写入日志，不中断 SweepAsync |
| OnStrategySweepCompleteAsync 返回 null | 跳过发送（payload 构建失败场景） |
| SWEEP_END 的 veneer 参数传 null | InfluxDbTransport 的 SWEEP_END 分支不使用该参数，安全 |

## YAML 变更

### 变更清单

| 文件 | 当前 handler | 目标 handler |
|---|---|---|
| `examples/config.system.yml`（锚点定义） | `BatteryHandler` | `TransportHandler` |
| `examples/config.machines.yml` | 继承锚点，自动生效 | 无需修改 |
| `examples/config.scanner.yml` | 基类 Handler | `TransportHandler` |
| `examples/config.fins.yml` | 基类 Handler | `TransportHandler` |
| `examples/config.opcua.yml` | 基类 Handler | `TransportHandler` |
| `examples/config.transport.influxdb.yml` | 基类 Handler | `TransportHandler` |

### config.system.yml 锚点说明

`config.system.yml` 定义 `&machine-base` YAML 锚点，`config.machines.yml` 通过 `<<: *machine-base` 继承。修改锚点的 handler 字段会级联影响所有引用该锚点的机器条目，只需改锚点一处，`config.machines.yml` 自动继承。

### config.system.yml 中的 BatteryHandler

当前 `BatteryHandler`（`src/CollectionDrivers.BatteryDriver/BatteryHandler.cs`）为空子类，无任何 override，功能等同于基类 Handler。替换为 `TransportHandler` 后获得 Transport 推送能力。`BatteryHandler` 类保留不动（标记 `[Obsolete]` 注释），防止已存 YAML 引用在切换过渡期报错。

### 统一 handler 值

所有 driver 统一使用：

```yaml
handler: CollectionDrivers.Common.TransportHandler, CollectionDrivers.Common
```

## 未纳入范围：DATA_ARRIVE

当前只处理 SWEEP_END，不处理 DATA_ARRIVE。理由：

- DATA_ARRIVE 依赖 Veneers 系统推送数据到 Handler，但当前 4 个 Strategy 均未使用 Veneers
- 为 DATA_ARRIVE 在各 Strategy 中嵌入 Veneers 改造成本高、收益低（各 Driver 仅有 1–5 个简单数据点）
- 将来某 Driver 需要按事件粒度推送时，再新增对应实现

## 实现文件

- `src/CollectionDrivers.Common/TransportHandler.cs` — 新建
- `examples/config.system.yml` — handler 字段修改
- `examples/config.scanner.yml` — handler 字段修改
- `examples/config.fins.yml` — handler 字段修改
- `examples/config.opcua.yml` — handler 字段修改
- `examples/config.transport.influxdb.yml` — handler 字段修改
- `src/CollectionDrivers.BatteryDriver/BatteryHandler.cs` — 添加 `[Obsolete]` 注释
