using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的采集 Scope。封装 IServiceScope + 组件生命周期。
/// 使用 ActivatorUtilities 组装 Machine → Strategy → Handler → Transport。
/// </summary>
internal class MachineScope : IMachineScope
{
    private readonly IServiceScope _scope;
    private readonly Strategy _strategy;
    private readonly Handler _handler;

    public IMachineContext Context { get; }

    internal MachineScope(
        IServiceScope scope,
        MachineOptions config,
        DriverTypeRegistry registry)
    {
        _scope = scope;
        var sp = scope.ServiceProvider;

        // Step 1: 查找类型注册
        var entry = registry.Find(config.DriverId)
                    ?? throw new InvalidOperationException(
                        $"No driver registered for DriverId '{config.DriverId ?? "(null)"}'");

        // Step 2: 绑定 Options
        var strategyOptions = BindOptions(
            config.Configuration?.GetSection("Strategy"), entry.StrategyOptionsType);
        var transportOptions = BindOptions(
            config.Configuration?.GetSection("Transport"), entry.TransportOptionsType);

        // Step 3: 构造 Machine
        var machineLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.MachineType));
        var machine = (Machine)ActivatorUtilities.CreateInstance(
            sp, entry.MachineType, machineLogger);
        machine.Initialize(config);
        Context = machine;

        // Step 4: 构造 Strategy
        var strategyLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.StrategyType));
        _strategy = (Strategy)ActivatorUtilities.CreateInstance(sp,
            entry.StrategyType, strategyLogger, Context, strategyOptions);
        _strategy.OnError += (ex, ctx) =>
            ((ILogger)strategyLogger).LogError(
                ex, "[{Id}] Strategy error in {Context}", Context.Id, ctx);

        // Step 5: 构造 Handler
        var handlerLogger = sp.GetRequiredService(
            typeof(ILogger<>).MakeGenericType(entry.HandlerType));
        _handler = (Handler)ActivatorUtilities.CreateInstance(sp,
            entry.HandlerType, handlerLogger, Context);

        // Step 6: 构造 Transport（单 Transport，后续可扩展为多 Transport）
        // TransportType 可能为 null（驱动未注册 Transport 时），跳过 Transport 创建
        List<Transport> transports;
        if (entry.TransportType != null)
        {
            var transportLogger = sp.GetRequiredService(
                typeof(ILogger<>).MakeGenericType(entry.TransportType));
            var transport = (Transport)ActivatorUtilities.CreateInstance(sp,
                entry.TransportType, transportLogger, Context, transportOptions);
            transports = new List<Transport> { transport };
        }
        else
        {
            transports = new List<Transport>();
        }

        // Step 7: 回挂到 Machine
        machine.SetStrategy(_strategy);
        machine.SetHandler(_handler);
        machine.SetTransports(transports);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // 生命周期：CreateAsync → InitializeAsync → SweepAsync 循环
        await _strategy.CreateAsync();
        foreach (var t in Context.Transports)
            await t.CreateAsync();
        await _handler.CreateAsync();

        await _strategy.InitializeAsync();
        while (!ct.IsCancellationRequested && Context.Enabled)
        {
            await _strategy.SweepAsync();
        }
        await Context.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        // 释放顺序：Transport → Strategy → Handler → Scope
        foreach (var t in Context.Transports)
        {
            if (t is IAsyncDisposable ad) await ad.DisposeAsync();
            else if (t is IDisposable d) d.Dispose();
        }
        if (_strategy is IAsyncDisposable sad) await sad.DisposeAsync();
        else if (_strategy is IDisposable sd) sd.Dispose();
        if (_handler is IAsyncDisposable had) await had.DisposeAsync();
        else if (_handler is IDisposable hd) hd.Dispose();
        _scope.Dispose();
    }

    private static object BindOptions(IConfiguration? section, Type optionsType)
    {
        var options = Activator.CreateInstance(optionsType)!;
        if (section != null)
        {
            section.Bind(options);
        }
        return options;
    }
}
