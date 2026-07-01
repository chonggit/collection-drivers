using CollectionDrivers.ScannerDriver.Models;
using CollectionDrivers.ScannerDriver.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// Scanner 驱动注册扩展。
/// </summary>
public static class ScannerDriverServiceExtensions
{
    /// <summary>注册 Scanner 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddScannerDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:            "Scanner",
            MachineType:         typeof(Machine),
            StrategyType:        typeof(ScannerStrategy),
            HandlerType:         typeof(TransportHandler),
            StrategyOptionsType:   typeof(ScannerStrategyOptions),
            TransportType:         null,
            TransportOptionsType:  null
        ));
        return services;
    }
}
