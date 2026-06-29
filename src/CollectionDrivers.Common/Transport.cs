#pragma warning disable CS1998

using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

public class Transport
{
    protected readonly ILogger Logger;

    // ReSharper disable once UnusedParameter.Local
    protected Transport(Machine machine)
    {
        Logger = LoggingFactory.CreateLogger(GetType().FullName);
        Machine = machine;
    }

    protected Machine Machine { get; }

    public virtual async Task<dynamic?> CreateAsync()
    {
        return null;
    }

    public virtual async Task ConnectAsync()
    {
    }

    public virtual async Task SendAsync(params dynamic[] parameters)
    {
    }
}
#pragma warning restore CS1998
