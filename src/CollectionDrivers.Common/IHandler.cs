namespace CollectionDrivers.Common;

/// <summary>
/// 采集完成后的数据处理契约。Strategy 每次 Sweep 完成后调用。
/// </summary>
public interface IHandler
{
    /// <summary>采集周期完成时调用</summary>
    Task OnStrategySweepCompleteInternalAsync();
}
