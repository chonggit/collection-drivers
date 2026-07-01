using CollectionDrivers.Common;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.FinsDriver;

public class FinsMachine : Machine
{
    public FinsMachine(Machines machines, object configuration)
        : base(machines, configuration) { }

    /// <summary>DI 构造函数</summary>
    public FinsMachine(ILogger? logger) : base(logger) { }
}
