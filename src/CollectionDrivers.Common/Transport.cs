using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

/// <summary>
/// 数据传输基类。Handler 处理后通过 Transport 将数据发送到外部系统。
/// 子类（如 InfluxDbTransport）实现具体传输协议。
/// </summary>
public class Transport
{
    /// <summary>日志记录器</summary>
    protected readonly ILogger Logger;

    /// <summary>构造 Transport，关联到指定 Machine</summary>
    // ReSharper disable once UnusedParameter.Local
    protected Transport(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    /// <summary>所属设备实例</summary>
    protected Machine Machine { get; }

    /// <summary>创建 Transport 资源。子类重写以初始化连接、解析配置等。</summary>
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
