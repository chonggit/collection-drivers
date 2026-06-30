using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Strategy
{
    protected readonly ILogger Logger;
    protected readonly int SweepMs;

    protected Strategy(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
        SweepMs = machine.Configuration.type["sweep_ms"];
    }

    public Machine Machine { get; }
    public bool LastSuccess { get; protected set; }
    public bool IsHealthy { get; protected set; }

    /// <summary>
    /// 策略执行过程中的错误事件。参数为异常和上下文描述。
    /// </summary>
    public event Action<Exception, string>? OnError;

    /// <summary>
    /// 供子类调用的 OnError 触发方法（C# 限制：派生类不能直接触发基类事件）。
    /// </summary>
    protected void RaiseOnError(Exception ex, string context)
    {
        OnError?.Invoke(ex, context);
    }

    public virtual Task CreateAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public virtual async Task SweepAsync(int delayMs = -1)
    {
        delayMs = delayMs < 0 ? SweepMs : delayMs;
        await Task.Delay(delayMs);
        LastSuccess = false;
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
