using CollectionDrivers.Common;
using Microsoft.Extensions.Logging;

namespace CollectionDrivers.ScannerDriver;

public class ScannerMachine : Machine
{
    public ScannerMachine(Machines machines, object configuration)
        : base(machines, configuration) { }

    /// <summary>DI 构造函数</summary>
    public ScannerMachine(ILogger? logger) : base(logger) { }
}
