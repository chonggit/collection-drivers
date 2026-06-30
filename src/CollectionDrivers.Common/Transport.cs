using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Transport
{
    protected readonly ILogger Logger;

    // ReSharper disable once UnusedParameter.Local
    protected Transport(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    protected Machine Machine { get; }

    public virtual Task CreateAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送事件数据到外部系统。
    /// </summary>
    /// <param name="eventName">事件名称，如 "SWEEP_END"</param>
    /// <param name="payload">事件负载数据</param>
    public virtual async Task SendAsync(string eventName, dynamic? payload)
    {
    }
}
