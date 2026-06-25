using Microsoft.Extensions.Hosting;
using opcua.driver.models;
using opcua.driver.strategies;
using l99.driver.@base;

namespace opcua.driver;

public class OpcUaDriverService : BackgroundService
{
    private readonly OpcUaConfig _config;
    private OpcUaStrategy? _strategy;

    public event Action<string, Dictionary<string, object>>? OnData;
    public event Action<Exception, string>? OnError;

    public OpcUaDriverService(OpcUaConfig config)
    {
        _config = config;
    }

    public OpcUaDriverService()
    {
        _config = new OpcUaConfig();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Note: YAML path uses Machines.CreateMachines(). This service is
        // for programmatic use where Machine is created internally.
        throw new NotImplementedException(
            "Use YAML configuration path with Machines.CreateMachines() for full setup");
    }

    internal OpcUaStrategy? Strategy => _strategy;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_strategy != null)
            await _strategy.DisposeAsync();
    }
}
