#pragma warning disable CS1998

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

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task<dynamic?> InitializeAsync()
    {
        return null;
    }

    protected virtual async Task<dynamic?> CollectAsync()
    {
        return null;
    }

    public virtual async Task SweepAsync(int delayMs = -1)
    {
        delayMs = delayMs < 0 ? SweepMs : delayMs;
        await Task.Delay(delayMs);
        LastSuccess = false;
        await CollectAsync();
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
