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
        Logger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    /// <summary>构造 Transport（DI 注入 Logger + 机器上下文）。</summary>
    // ReSharper disable once UnusedParameter.Local
    protected Transport(ILogger? logger, Machine machine)
    {
        Logger = logger!;
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
    /// 发送事件数据到外部系统。子类根据 eventName 判断 payload 具体类型（如 "SWEEP_END" → SweepEndPayload），
    /// 使用模式匹配或显式类型转换访问字段。
    /// </summary>
    /// <param name="eventName">事件名称，如 "SWEEP_END"</param>
    /// <param name="payload">事件负载数据</param>
    public virtual Task SendAsync(string eventName, object? payload)
    {
        return Task.CompletedTask;
    }
}
