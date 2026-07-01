namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的采集 Scope。封装 IServiceScope + 组件生命周期。
/// </summary>
public interface IMachineScope : IAsyncDisposable
{
    /// <summary>机器上下文</summary>
    IMachineContext Context { get; }

    /// <summary>
    /// 启动采集循环：CreateAsync → InitializeAsync → SweepAsync 循环。
    /// 循环在 ct 取消或 Context.Enabled 变为 false 时退出。
    /// </summary>
    Task RunAsync(CancellationToken ct);
}
