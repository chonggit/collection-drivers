#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Handler
{
    protected readonly ILogger Logger;

    protected Handler(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    public Machine Machine { get; }

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task OnStrategySweepCompleteInternalAsync()
    {
        var onRet = await OnStrategySweepCompleteAsync(Machine);
        await AfterSweepCompleteAsync(Machine, onRet);
    }

    protected virtual async Task<dynamic?> OnStrategySweepCompleteAsync(Machine machine)
    {
        return null;
    }

    protected virtual async Task AfterSweepCompleteAsync(Machine machine, dynamic? onSweepComplete)
    {
    }
}
