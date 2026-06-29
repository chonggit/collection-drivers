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
