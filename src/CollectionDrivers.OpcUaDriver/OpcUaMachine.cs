using CollectionDrivers.Common;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.OpcUaDriver;

public class OpcUaMachine : Machine
{
    public OpcUaMachine(Machines machines, object configuration)
        : base(machines, configuration) { }

    /// <summary>DI 构造函数</summary>
    public OpcUaMachine(ILogger? logger) : base(logger) { }
}
