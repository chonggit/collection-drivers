// ReSharper disable once CheckNamespace

using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 设备抽象基类。管理 Strategy（采集）、Handler（处理）、Transport（发送）三大组件的生命周期，
/// 承载设备配置和运行状态。子类（如 BatteryMachine、ScannerMachine）通过构造函数注入具体类型。
/// </summary>
public abstract class Machine : IAsyncDisposable
{
    protected readonly ILogger Logger;

    /// <summary>
    /// 构造设备实例。从 configuration 中读取 machine.enabled 和 machine.id。
    /// </summary>
    /// <param name="machines">设备集合（历史参数，未使用）</param>
    /// <param name="configuration">YAML 配置反序列化后的动态对象</param>
    protected Machine(Machines machines, object configuration)
    {
        Configuration = configuration;
        Enabled = Configuration.machine.enabled;
        Logger = LoggingFactory.CreateLogger(typeof(Machine).FullName);
        Logger.LogDebug($"[{Id}] Creating machine, enabled: {Enabled}");
    }

    /// <summary>设备配置（YAML 反序列化的 dynamic 对象）</summary>
    public dynamic Configuration { get; }

    /// <summary>设备是否启用。false 时采集循环不会启动，运行时设为 false 会停止采集。</summary>
    public bool Enabled { get; private set; }

    /// <summary>设备标识符（来自配置 machine.id）</summary>
    public string Id => Configuration.machine.id;

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

    }

    /// <summary>
    /// 异步释放所有组件资源（Transport → Strategy → Handler）。
    /// 先调用 Stop() 优雅停止，再依次释放子组件。
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        await Stop();

        // 释放所有 Transport
        foreach (var t in _transports)
        {
            if (t is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (t is IDisposable d) d.Dispose();
        }

        // 释放 Strategy
        if (Strategy is IAsyncDisposable sad) await sad.DisposeAsync();
        else if (Strategy is IDisposable sd) sd.Dispose();

        // 释放 Handler
        if (Handler is IAsyncDisposable had) await had.DisposeAsync();
        else if (Handler is IDisposable hd) hd.Dispose();
    }

    #region handler

    /// <summary>数据处理组件。Strategy 每次采集完成后调用其 OnStrategySweepCompleteInternalAsync。</summary>
    public Handler Handler { get; private set; } = null!;

    /// <summary>
    /// 通过反射创建并添加 Handler。类型字符串来自 YAML 配置的 handler 字段。
    /// 创建失败时自动调用 Disable()。
    /// </summary>
    /// <param name="type">Handler 的具体类型</param>
    public async Task<Machine> AddHandlerAsync(Type type)
    {
        if (type == null)
        {
            Logger.LogError("[{Id}] Cannot add handler: type is null", Id);
            Disable();
            return this;
        }

        Logger.LogDebug($"[{Id}] Creating handler: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Handler = (Handler) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Handler!.CreateAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Unable to add handler: {Type}", Id, type.FullName);
            Disable();
        }

        return this;
    }

    #endregion

    #region strategy

    /// <summary>策略上次采集是否成功</summary>
    public bool StrategySuccess => Strategy?.LastSuccess ?? false;
    /// <summary>策略当前是否健康</summary>
    public bool StrategyHealthy => Strategy?.IsHealthy ?? false;
    /// <summary>数据采集策略组件。负责与设备通信、读取数据。</summary>
    public Strategy Strategy { get; private set; } = null!;

    /// <summary>
    /// 通过反射创建并添加 Strategy。类型字符串来自 YAML 配置的 strategy 字段。
    /// 创建失败时自动调用 Disable()。
    /// </summary>
    /// <param name="type">Strategy 的具体类型</param>
    public async Task<Machine> AddStrategyAsync(Type type)
    {
        if (type == null)
        {
            Logger.LogError("[{Id}] Cannot add strategy: type is null", Id);
            Disable();
            return this;
        }

        Logger.LogDebug($"[{Id}] Creating strategy: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Strategy = (Strategy) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            // 订阅策略错误事件，确保异常通过日志可见（而非通过无订阅者的 OnError 事件静默丢弃）
            Strategy!.OnError += (ex, context) =>
                Logger.LogError(ex, "[{Id}] Strategy error in {Context}", Id, context);

            await Strategy!.CreateAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Unable to add strategy: {Type}", Id, type.FullName);
            Disable();
        }

        return this;
    }

    /// <summary>初始化策略（调用 Strategy.InitializeAsync）</summary>
    public async Task InitStrategyAsync()
    {
        Logger.LogDebug($"[{Id}] Initializing strategy...");
        if (Strategy != null) await Strategy.InitializeAsync();
    }

    /// <summary>执行一次策略采集（调用 Strategy.SweepAsync）</summary>
    public async Task RunStrategyAsync()
    {
        if (Strategy != null) await Strategy.SweepAsync();
    }

    #endregion

    #region transport

    private readonly List<Transport> _transports = new();

    /// <summary>所有已注册的数据发送组件。Handler 处理后遍历此列表将数据推送到各外部系统。</summary>
    public IReadOnlyList<Transport> Transports => _transports;

    /// <summary>
    /// 首选 Transport（向后兼容）。返回第一个已注册的 Transport，未注册时返回 null。
    /// 多 Transport 场景请使用 Transports 属性遍历。
    /// </summary>
    public Transport? Transport => _transports.FirstOrDefault();

    /// <summary>
    /// 通过反射创建并添加 Transport。类型字符串来自 YAML 配置的 transport 字段。
    /// 支持多次调用以注册多个 Transport（如同时输出到 InfluxDB + MQTT）。
    /// 创建失败时自动调用 Disable()。
    /// </summary>
    /// <param name="type">Transport 的具体类型</param>
    public async Task<Machine> AddTransportAsync(Type type)
    {
        if (type == null)
        {
            Logger.LogError("[{Id}] Cannot add transport: type is null", Id);
            Disable();
            return this;
        }

        Logger.LogDebug($"[{Id}] Creating transport: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            var transport = (Transport) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await transport!.CreateAsync();
            _transports.Add(transport);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Unable to add transport: {Type}", Id, type.FullName);
            Disable();
        }

        return this;
    }

    #endregion
}
