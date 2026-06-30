using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

/// <summary>
/// 数据采集策略基类。子类（BatteryTcpStrategy、FinsStrategy、OpcUaStrategy、ScannerStrategy）
/// 实现具体协议的采集逻辑。通过 CreateAsync → InitializeAsync → SweepAsync 生命周期管理设备通信。
/// </summary>
public class Strategy
{
    protected readonly ILogger Logger;
    /// <summary>采集间隔（毫秒），从配置 type.sweep_ms 读取</summary>
    protected readonly int SweepMs;

    /// <summary>构造策略实例，从 Machine 配置中读取 sweeep 间隔</summary>
    protected Strategy(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
        SweepMs = machine.Configuration.type["sweep_ms"];
    }

    /// <summary>所属设备实例</summary>
    public Machine Machine { get; }
    /// <summary>上次采集是否成功</summary>
    public bool LastSuccess { get; protected set; }
    /// <summary>策略当前是否健康</summary>
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

    /// <summary>创建策略资源。子类可重写以初始化连接等。</summary>
    public virtual Task CreateAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>初始化策略。子类重写以建立连接、订阅等。</summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 执行一次采集周期。延迟 delayMs 后采集数据，设置 LastSuccess/IsHealthy，
    /// 最后调用 Handler.OnStrategySweepCompleteInternalAsync 触发数据处理管线。
    /// </summary>
    /// <param name="delayMs">延迟毫秒数，-1 使用默认 SweepMs</param>
    public virtual async Task SweepAsync(int delayMs = -1)
    {
        delayMs = delayMs < 0 ? SweepMs : delayMs;
        await Task.Delay(delayMs);
        LastSuccess = false;
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
