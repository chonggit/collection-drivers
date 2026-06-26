using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

// ReSharper disable once ClassNeverInstantiated.Global
public class Bootstrap
{
    private static ILogger _logger = NullLogger.Instance;

#pragma warning disable CS1998
    public static async Task Stop()
    {
        LoggingFactory.Close();
    }
#pragma warning restore CS1998

    public static async Task<dynamic> Start(string[] args)
    {
        DetectArch();
        var configFiles = GetArgument(args, "--config", "config.system.yml,config.user.yml,config.machines.yml");
        var config = ReadConfig(configFiles.Split(','));
        _logger = LoggingFactory.CreateLogger(typeof(Bootstrap).FullName);
        _logger.LogInformation("Configuration loaded");
        return config;
    }

    private static void DetectArch()
    {
        Console.WriteLine($"Bitness: {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}");
    }

    private static string GetArgument(string[] args, string optionName, string defaultValue)
    {
        var value = args.SkipWhile(i => i != optionName).Skip(1).Take(1).FirstOrDefault();
        var optionValue = string.IsNullOrEmpty(value) ? defaultValue : value;
        Console.WriteLine($"Argument '{optionName}' = '{optionValue}'");
        return optionValue;
    }

    private static dynamic ReadConfig(string[] configFiles)
    {
        var yaml = "";
        foreach (var configFile in configFiles) yaml += File.ReadAllText(configFile);

        var stringReader = new StringReader(yaml);
        var parser = new Parser(stringReader);
        var mergingParser = new MergingParser(parser);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var config = deserializer.Deserialize(mergingParser);

        _logger.LogTrace("Deserialized configuration: {Config}",
            JObject.FromObject(config ?? throw new InvalidOperationException("Configuration cannot be null.")));
        
        return config;
    }
}