using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CollectionDrivers.Common;

/// <summary>
/// IServiceCollection 扩展方法。宿主调用以注册驱动基础设施。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册驱动基础设施：绑定配置、注册 IMachineScopeFactory、注册 DriverHostService。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configurationSection">"CollectionDrivers" 配置段</param>
    public static IServiceCollection AddCollectionDrivers(
        this IServiceCollection services,
        IConfiguration configurationSection)
    {
        services.Configure<CollectionDriverOptions>(configurationSection);
        services.AddSingleton<IMachineScopeFactory, MachineScopeFactory>();
        services.AddHostedService<DriverHostService>();
        return services;
    }
}
