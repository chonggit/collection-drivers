using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

/// <summary>
/// 数据处理基类。Strategy 每次采集完成后调用 OnStrategySweepCompleteInternalAsync。
/// 子类（如 TransportHandler）重写以构建 payload 并通过 Transport 发送到外部系统。
/// </summary>
public class Handler : IHandler
{
    /// <summary>日志记录器</summary>
    protected readonly ILogger Logger = NullLogger.Instance;

    /// <summary>构造 Handler，关联到指定 Machine</summary>
    protected Handler(Machine machine)
    {
        Machine = machine;
    }

    /// <summary>
    /// 构造 Handler（DI 注入 Logger + 机器上下文）。
    /// </summary>
    protected Handler(ILogger? logger, Machine machine)
    {
        Logger = logger!;
        Machine = machine;
    }

    /// <summary>所属设备实例</summary>
    public Machine Machine { get; }

    /// <summary>创建 Handler 资源</summary>
    public virtual Task CreateAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 策略采集周期完成时调用。基类默认无操作，子类（如 TransportHandler）可重写
    /// 以构建 payload 并通过 Transport 发送。
    /// </summary>
    public virtual Task OnStrategySweepCompleteInternalAsync()
    {
        return Task.CompletedTask;
    }
}
