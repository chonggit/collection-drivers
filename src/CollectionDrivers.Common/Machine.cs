// ReSharper disable once CheckNamespace

using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

public abstract class Machine
{
    protected readonly ILogger Logger;

    protected Machine(Machines machines, object configuration)
    {
        Configuration = configuration;
        Enabled = Configuration.machine.enabled;
        Logger = LoggingFactory.CreateLogger(typeof(Machine).FullName);
        Logger.LogDebug($"[{Id}] Creating machine, enabled: {Enabled}");
        _propertyBag = new Dictionary<string, dynamic>();
    }

    public dynamic Configuration { get; }

    public bool Enabled { get; private set; }

    public string Id => Configuration.machine.id;

    public override string ToString()
    {
        return new {Id}.ToString()!;
    }

    public void Disable()
    {
        Enabled = false;
    }

    public virtual async Task Stop()
    {

    }

    #region property-bag

    private readonly Dictionary<string, dynamic> _propertyBag;

    public dynamic? this[string propertyBagKey]
    {
        get
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
                return _propertyBag[propertyBagKey];
            return null;
        }

        // ReSharper disable once PropertyCanBeMadeInitOnly.Global
        // ReSharper disable once MemberCanBeProtected.Global
        set
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
            {
                _propertyBag[propertyBagKey] = value!;
            }
            else
            {
                Logger.LogDebug($"[{Id}] Adding '{propertyBagKey}' to property bag.");
                _propertyBag.Add(propertyBagKey, value);
            }
        }
    }

    #endregion

    #region handler

    public Handler Handler { get; private set; } = null!;

    public async Task<Machine> AddHandlerAsync(Type type)
    {
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

    public bool StrategySuccess => Strategy?.LastSuccess ?? false;
    public bool StrategyHealthy => Strategy?.IsHealthy ?? false;
    public Strategy Strategy { get; private set; } = null!;

    public async Task<Machine> AddStrategyAsync(Type type)
    {
        Logger.LogDebug($"[{Id}] Creating strategy: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Strategy = (Strategy) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Strategy!.CreateAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Id}] Unable to add strategy: {Type}", Id, type.FullName);
            Disable();
        }

        return this;
    }

    public async Task InitStrategyAsync()
    {
        Logger.LogDebug($"[{Id}] Initializing strategy...");
        if (Strategy != null) await Strategy.InitializeAsync();
    }

    public async Task RunStrategyAsync()
    {
        if (Strategy != null) await Strategy.SweepAsync();
    }

    #endregion

    #region transport

    public Transport Transport { get; private set; } = null!;

    public async Task<Machine> AddTransportAsync(Type type)
    {
        Logger.LogDebug($"[{Id}] Creating transport: {type.FullName}");

        try
        {
#pragma warning disable CS8600, CS8601
            Transport = (Transport) Activator.CreateInstance(type, this);
#pragma warning restore CS8600, CS8601

            await Transport!.CreateAsync();
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
