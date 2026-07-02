// ReSharper disable once CheckNamespace

using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CollectionDrivers.Common;

public class Machines
{
    private readonly ILogger _logger = NullLogger.Instance;
    private readonly List<Machine> _machines;

    private Machines()
    {
        _machines = new List<Machine>();
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
            Machine machine = (Machine)Activator.CreateInstance(machineType, new object[] { this, configuration })!;
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

        while (!stoppingToken.IsCancellationRequested && machine.Enabled)
        {
            await machine.RunStrategyAsync();
        }

        _logger.LogInformation($"[{machine.Id}] Machine task stopping");

        await machine.Stop();
        await machine.DisposeAsync();

        _logger.LogInformation($"[{machine.Id}] Machine task stopped");
    }
}
