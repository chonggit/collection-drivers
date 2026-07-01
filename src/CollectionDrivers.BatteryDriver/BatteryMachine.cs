using CollectionDrivers.Common;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.BatteryDriver;

public class BatteryMachine : Machine
{
    public BatteryMachine(Machines machines, object configuration) : base(machines, configuration)
    {
    }

    /// <summary>DI 构造函数</summary>
    public BatteryMachine(ILogger? logger) : base(logger) { }
}
