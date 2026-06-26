using Microsoft.Extensions.Hosting;
using fins.driver.models;
using fins.driver.strategies;

namespace fins.driver;

public class FinsDriverService : BackgroundService
{
    private readonly FinsConfig _config;
    private FinsStrategy? _strategy;

    public event Action<string, ushort[]>? OnData;
    public event Action<Exception, string>? OnError;

    public FinsDriverService(FinsConfig config)
    {
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException(
            "Use YAML configuration path with Machines.CreateMachines() for full setup");
    }

    internal FinsStrategy? Strategy => _strategy;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _strategy?.DisposeConnection();
    }
}
