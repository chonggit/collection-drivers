// ReSharper disable once CheckNamespace

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CollectionDrivers.Common;

/// <summary>
/// 设备类。管理 Strategy（采集）、Handler（处理）、Transport（发送）三大组件的生命周期，
/// 承载设备配置和运行状态。实现 IMachineContext 以切断循环依赖。
/// </summary>
public class Machine : IMachineContext, IAsyncDisposable
{
    protected readonly ILogger Logger = NullLogger.Instance;

    private string? _id;
    private bool _enabled;
    private int _sweepMs;
    private Strategy? _strategy;
    private Handler? _handler;
    private List<Transport> _transports = new();

    /// <summary>
    /// 构造设备实例。从 configuration 中读取 machine.enabled 和 machine.id。
    /// </summary>
    /// <param name="machines">设备集合（历史参数，未使用）</param>
    /// <param name="configuration">YAML 配置反序列化后的动态对象</param>
    public Machine(Machines machines, object configuration)
    {
        Configuration = configuration;
        _enabled = Configuration.machine.enabled;
        Logger.LogDebug($"[{Id}] Creating machine, enabled: {Enabled}");
    }

    /// <summary>DI 构造函数：仅注入 Logger。</summary>
    protected Machine(ILogger? logger)
    {
        Logger = logger!;
    }

    /// <summary>设备配置（YAML 反序列化的 dynamic 对象）。迁移期间保留，Phase 5 删除。</summary>
    public dynamic? Configuration { get; }

    // ── IMachineContext 实现 ──

    /// <summary>设备标识符。迁移期间优先 _id，fallback 到 Configuration</summary>
    public string Id => _id ?? Configuration?.machine?.id ?? "";

    /// <summary>设备是否启用。</summary>
    public bool Enabled
    {
        get => _enabled;
        private set => _enabled = value;
    }

    /// <summary>采集间隔（毫秒）。迁移期间优先 _sweepMs，fallback 到 Configuration</summary>
    public int SweepMs => _sweepMs > 0
        ? _sweepMs
        : (Configuration?.type?["sweep_ms"] ?? 5000);

    /// <summary>数据处理组件（IHandler 接口）。返回 null 当 Handler 未设置。</summary>
    public IHandler? Handler => _handler;

    /// <summary>所有已注册的数据发送组件</summary>
    public IReadOnlyList<Transport> Transports => _transports;

    /// <summary>Strategy 上次采集是否成功</summary>
    public bool StrategySuccess => _strategy?.LastSuccess ?? false;

    /// <summary>Strategy 当前是否健康</summary>
    public bool StrategyHealthy => _strategy?.IsHealthy ?? false;

    /// <inheritdoc/>
    public override string ToString()
    {
        return new {Id}.ToString()!;
    }

    /// <summary>禁用设备。运行中的采集循环将在下一次迭代检测到并退出。</summary>
    public void Disable()
    {
        Enabled = false;
    }

    /// <summary>停止设备。子类可重写以添加自定义停止逻辑。</summary>
    public virtual async Task Stop()
    {
        await Task.CompletedTask;
    }

    // ── 新方法（替代 dynamic 配置 + 属性注入） ──

    /// <summary>用 MachineOptions 初始化 Machine 状态（替代构造函数中的 dynamic 配置）</summary>
    public void Initialize(MachineOptions options)
    {
        _sweepMs = options.SweepMs;
        _enabled = options.Enabled;
        _id = options.Id;
    }

    /// <summary>回挂 Strategy 实例（由 MachineScope 调用）</summary>
    internal void SetStrategy(Strategy strategy) => _strategy = strategy;

    /// <summary>回挂 Handler 实例</summary>
    internal void SetHandler(Handler handler) => _handler = handler;

    /// <summary>回挂 Transport 列表</summary>
    internal void SetTransports(List<Transport> transports) => _transports = transports;

    // ── 向后兼容属性 ──

    /// <summary>数据采集策略组件（向后兼容，Phase 5 移除）</summary>
    public Strategy Strategy
    {
        get => _strategy ?? throw new InvalidOperationException("Strategy not set");
        private set => _strategy = value;
    }

    /// <summary>数据处理组件（具体类型，向后兼容）</summary>
    public Handler HandlerInstance
    {
        get => _handler ?? throw new InvalidOperationException("Handler not set");
        private set => _handler = value;
    }

    /// <summary>
    /// 异步释放所有组件资源（Transport → Strategy → Handler）。
    /// 先调用 Stop() 优雅停止，再依次释放子组件。
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        await Stop();

        foreach (var t in _transports)
        {
            if (t is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (t is IDisposable d) d.Dispose();
        }

        if (_strategy is IAsyncDisposable sad) await sad.DisposeAsync();
        else if (_strategy is IDisposable sd) sd.Dispose();

        if (_handler is IAsyncDisposable had) await had.DisposeAsync();
        else if (_handler is IDisposable hd) hd.Dispose();
    }
}
