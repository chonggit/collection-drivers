using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// Battery 驱动注册扩展。
/// </summary>
public static class BatteryDriverServiceExtensions
{
    /// <summary>注册 Battery 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddBatteryDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:            "Battery",
            MachineType:         typeof(Machine),
            StrategyType:        typeof(BatteryTcpStrategy),
            HandlerType:         typeof(TransportHandler),
            StrategyOptionsType: typeof(BatteryTcpStrategyOptions),
            TransportType:       null,
            TransportOptionsType: null
        ));
        return services;
    }
}
