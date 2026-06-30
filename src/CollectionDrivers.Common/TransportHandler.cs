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
    /// 构建 SWEEP_END payload 并通过所有已注册的 Transport 发送。
    /// 支持同时输出到多个 Transport（如 InfluxDB + MQTT）。
    /// 单个 Transport 发送失败时仅记录日志，不阻断其他 Transport 的发送。
    /// </summary>
    public override async Task OnStrategySweepCompleteInternalAsync()
    {
        var transports = Machine.Transports;
        if (transports.Count == 0) return;

        var payload = new SweepEndPayload(
            Observation: new SweepEndObservation(
                Time: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Machine: Machine.Id,
                Name: "sweep"
            ),
            Online: Machine.StrategySuccess,
            Healthy: Machine.StrategyHealthy
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
                    Machine.Id, transport.GetType().Name);
            }
        }
    }
}
