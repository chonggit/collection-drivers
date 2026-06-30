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
    /// 构建 SWEEP_END payload 并通过 Transport 发送。
    /// Transport 为 null 或 SendAsync 异常时安全降级，不中断采集循环。
    /// </summary>
    public override async Task OnStrategySweepCompleteInternalAsync()
    {
        if (Machine.Transport == null) return;

        var payload = new
        {
            observation = new
            {
                time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                machine = Machine.Id,
                name = "sweep"
            },
            state = new
            {
                data = new
                {
                    online = Machine.StrategySuccess,
                    healthy = Machine.StrategyHealthy
                }
            }
        };

        try
        {
            await Machine.Transport.SendAsync("SWEEP_END", payload);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{MachineId}] Transport SWEEP_END send failed", Machine.Id);
        }
    }
}
