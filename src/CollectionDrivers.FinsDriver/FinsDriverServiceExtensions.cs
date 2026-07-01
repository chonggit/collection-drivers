using CollectionDrivers.FinsDriver.Models;
using CollectionDrivers.FinsDriver.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// FINS 驱动注册扩展。
/// </summary>
public static class FinsDriverServiceExtensions
{
    /// <summary>注册 FINS 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddFinsDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:            "Fins",
            MachineType:         typeof(Machine),
            StrategyType:        typeof(FinsStrategy),
            HandlerType:         typeof(TransportHandler),
            StrategyOptionsType: typeof(FinsStrategyOptions)
        ));
        return services;
    }
}
