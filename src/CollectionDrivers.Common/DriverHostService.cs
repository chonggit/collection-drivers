using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动宿主服务。通过 .NET IConfiguration 解析配置文件路径（支持环境变量覆盖），
/// 使用 YamlDotNet 解析 YAML 内容并启动所有机器采集循环。
/// 初始化时将宿主提供的 ILoggerFactory 注入全局 LoggingFactory。
/// </summary>
public class DriverHostService : BackgroundService
{
    /// <summary>
    /// 构造函数注入 ILoggerFactory，桥接到 LoggingFactory 静态工厂，
    /// 使所有驱动组件（Strategy/Handler/Transport/Machine）获得结构化日志输出。
    /// </summary>
    public DriverHostService(ILoggerFactory loggerFactory)
    {
        LoggingFactory.SetProvider(loggerFactory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 使用标准 .NET 配置管道解析配置文件路径
        // 优先级：命令行 > 环境变量 > 默认值
        var appConfig = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var yamlConfigPath = appConfig["COLLECTION_DRIVERS_CONFIG"]
            ?? "config.machines.yml";

        var yaml = await File.ReadAllTextAsync(yamlConfigPath, stoppingToken);

        var deserializer = new DeserializerBuilder().Build();
        var parser = new Parser(new StringReader(yaml));
        var mergingParser = new MergingParser(parser);
        var config = deserializer.Deserialize(mergingParser);

        var machines = await Machines.CreateMachines(config);
        await machines.RunAsync(stoppingToken);
    }
}
