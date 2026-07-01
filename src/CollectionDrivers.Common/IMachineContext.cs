namespace CollectionDrivers.Common;

/// <summary>
/// Strategy/Handler/Transport 对 Machine 的只读视图。
/// 切断 Machine ↔ Strategy 循环依赖。
/// </summary>
public interface IMachineContext
{
    /// <summary>机器标识符</summary>
    string Id { get; }

    /// <summary>是否启用</summary>
    bool Enabled { get; }

    /// <summary>采集间隔（毫秒）</summary>
    int SweepMs { get; }

    /// <summary>数据处理组件。未设置时可能为 null。</summary>
    IHandler? Handler { get; }

    /// <summary>所有已注册的数据发送组件</summary>
    IReadOnlyList<Transport> Transports { get; }

    /// <summary>Strategy 上次采集是否成功</summary>
    bool StrategySuccess { get; }

    /// <summary>Strategy 当前是否健康</summary>
    bool StrategyHealthy { get; }

    /// <summary>停止设备，运行中的采集循环由此退出</summary>
    Task Stop();
}
