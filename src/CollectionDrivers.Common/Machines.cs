// ReSharper disable once CheckNamespace

using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

public class Machines
{
    private readonly ILogger _logger;
    private readonly List<Machine> _machines;
    private readonly Dictionary<string, dynamic> _propertyBag;

    private Machines()
    {
        _logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);
        _machines = new List<Machine>();
        _propertyBag = new Dictionary<string, dynamic>();
    }

    public dynamic? this[string propertyBagKey]
    {
        get
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
                return _propertyBag[propertyBagKey];
            return null;
        }

        set
        {
            if (_propertyBag.ContainsKey(propertyBagKey))
            {
#pragma warning disable CS8601
                _propertyBag[propertyBagKey] = value;
#pragma warning restore CS8601
            }
            else
            {
                _propertyBag.Add(propertyBagKey, value);
            }
        }
    }

    private Machine? Add(dynamic configuration)
    {
        if (configuration.machine.enabled == false)
        {
            _logger.LogInformation($"[{configuration.machine.id}] Machine disabled and will not be added");
            return null;
        }

        _logger.LogDebug($"Adding machine:\n{JObject.FromObject(configuration.machine).ToString()}");

        try
        {
            Type machineType = Type.GetType(configuration.machine.type);
            Machine machine = (Machine) Activator.CreateInstance(machineType, new object[] {this, configuration})!;
            _machines.Add(machine);
            return machine;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[{MachineId}] Failed to add machine", (string?)configuration.machine.id);
            return null;
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();

        foreach (var machine in _machines.Where(x => x.Enabled))
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await RunMachineAsync(machine, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MachineId}] Machine task crashed", machine.Id);
                }
            }, stoppingToken));
        }

        _logger.LogInformation("Machine tasks running");

        await Task.WhenAll(tasks);

        _logger.LogInformation("Machine tasks stopped");
    }

    private async Task RunMachineAsync(Machine machine, CancellationToken stoppingToken)
    {
        await machine.InitStrategyAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await machine.RunStrategyAsync();
        }

        _logger.LogInformation($"[{machine.Id}] Machine task stopping");

        await machine.Stop();

        _logger.LogInformation($"[{machine.Id}] Machine task stopped");
    }

    public static async Task<Machines> CreateMachines(dynamic config)
    {
        var logger = LoggingFactory.CreateLogger(typeof(Machines).FullName);

        var machineConfigs = new List<dynamic>();

        foreach (var machineConf in config["machines"])
        {
            var prebuiltConfig = new
            {
                machine = new
                {
                    id = machineConf.ContainsKey("id") ? machineConf["id"] : Guid.NewGuid().ToString(),
                    enabled = machineConf.ContainsKey("enabled") ? machineConf["enabled"] : false,
                    type = machineConf.ContainsKey("type")
                        ? machineConf["type"]
                        : null,
                    strategy = machineConf.ContainsKey("strategy")
                        ? machineConf["strategy"]
                        : null,
                    handler = machineConf.ContainsKey("handler")
                        ? machineConf["handler"]
                        : null,
                    transport = machineConf.ContainsKey("transport")
                        ? machineConf["transport"]
                        : null
                }
            };

            var builtConfig = new
            {
                prebuiltConfig.machine,
                type = prebuiltConfig.machine.type != null && machineConf.ContainsKey(prebuiltConfig.machine.type)
                    ? machineConf[prebuiltConfig.machine.type]
                    : new Dictionary<object, object>(),
                strategy = prebuiltConfig.machine.strategy != null && machineConf.ContainsKey(prebuiltConfig.machine.strategy)
                    ? machineConf[prebuiltConfig.machine.strategy]
                    : new Dictionary<object, object>(),
                handler = prebuiltConfig.machine.handler != null && machineConf.ContainsKey(prebuiltConfig.machine.handler)
                    ? machineConf[prebuiltConfig.machine.handler]
                    : new Dictionary<object, object>(),
                transport = prebuiltConfig.machine.transport != null && machineConf.ContainsKey(prebuiltConfig.machine.transport)
                    ? machineConf[prebuiltConfig.machine.transport]
                    : new Dictionary<object, object>()
            };

            // ReSharper disable once RedundantToStringCall
            logger.LogTrace($"Machine configuration built:\n{JObject.FromObject(builtConfig)}");

            machineConfigs.Add(builtConfig);
        }

        var machines = new Machines();

        foreach (var cfg in machineConfigs)
        {
            logger.LogTrace($"Creating machine from config:\n{JObject.FromObject(cfg)}");

            Machine machine = machines.Add(cfg);

            if (machine != null)
            {
                try
                {
                    Type transportType = Type.GetType(cfg.machine.transport);
                    Type strategyType = Type.GetType(cfg.machine.strategy);
                    Type handlerType = Type.GetType(cfg.machine.handler);

                    await machine.AddTransportAsync(transportType);
                    await machine.AddStrategyAsync(strategyType);
                    await machine.AddHandlerAsync(handlerType);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "[{MachineId}] Failed to create machine", machine.Id);
                    machine.Disable();
                }
            }
        }

        return machines;
    }
}
