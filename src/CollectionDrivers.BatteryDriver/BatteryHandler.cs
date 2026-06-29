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
