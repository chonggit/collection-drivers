using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Handler
{
    protected readonly ILogger Logger;

    protected Handler(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    public Machine Machine { get; }

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
