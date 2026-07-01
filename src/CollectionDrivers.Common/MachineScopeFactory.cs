using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// IMachineScopeFactory 的生产实现。注入 DI 基础设施并为每台机器创建独立 Scope。
/// </summary>
internal class MachineScopeFactory : IMachineScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DriverTypeRegistry _registry;
    private readonly ILogger<MachineScopeFactory> _logger;

    public MachineScopeFactory(
        IServiceScopeFactory scopeFactory,
        IEnumerable<DriverTypeRegistry.Entry> entries,
        ILogger<MachineScopeFactory> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _registry = new DriverTypeRegistry();
        foreach (var e in entries)
        {
            if (_registry.Entries.Any(x => x.DriverId == e.DriverId))
                _logger.LogWarning("Duplicate DriverId '{DriverId}' registered, first-wins", e.DriverId);
            else
                _registry.Entries.Add(e);
        }
        _logger.LogInformation("DriverTypeRegistry initialized with {Count} entries", _registry.Entries.Count);
    }

    /// <summary>为指定机器配置创建独立的采集 Scope</summary>
    public IMachineScope CreateScope(MachineOptions config)
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            return new MachineScope(scope, config, _registry);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
