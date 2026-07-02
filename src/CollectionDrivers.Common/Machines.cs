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
}
