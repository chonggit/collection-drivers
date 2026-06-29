// using l99.driver.@base — removed, same namespace
using Microsoft.Extensions.Hosting;
using YamlDotNet.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace CollectionDrivers.Common;

public class DriverHostService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var yamlConfigPath = Environment.GetEnvironmentVariable("COLLECTION_DRIVERS_CONFIG")
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
