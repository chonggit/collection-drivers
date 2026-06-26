using Microsoft.Extensions.Hosting;
using scanner.driver.models;
using scanner.driver.strategies;

namespace scanner.driver;

public class ScannerDriverService : BackgroundService
{
    private readonly ScannerConfig _config;
    private ScannerStrategy? _strategy;

    public event Action<string, string>? OnData;
    public event Action<Exception, string>? OnError;

    public ScannerDriverService(ScannerConfig config)
    {
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException(
            "Use YAML configuration path with Machines.CreateMachines() for full setup");
    }

    internal ScannerStrategy? Strategy => _strategy;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
    }
}
