using CollectionDrivers.OpcUaDriver.Models;
using CollectionDrivers.OpcUaDriver.Strategies;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// OPC UA 驱动注册扩展。
/// </summary>
public static class OpcUaDriverServiceExtensions
{
    /// <summary>注册 OPC UA 驱动类型到 DriverTypeRegistry</summary>
    public static IServiceCollection AddOpcUaDriver(this IServiceCollection services)
    {
        services.AddSingleton(_ => new DriverTypeRegistry.Entry(
            DriverId:            "OpcUa",
            MachineType:         typeof(Machine),
            StrategyType:        typeof(OpcUaStrategy),
            HandlerType:         typeof(TransportHandler),
            StrategyOptionsType:   typeof(OpcUaStrategyOptions),
            TransportType:         null,
            TransportOptionsType:  null
        ));
        return services;
    }
}
