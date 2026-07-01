namespace CollectionDrivers.FinsDriver.Models;

/// <summary>
/// FINS UDP 策略配置。
/// </summary>
public class FinsStrategyOptions
{
    /// <summary>远程 IP 地址</summary>
    public string RemoteIp { get; set; } = "192.168.1.1";

    /// <summary>端口号</summary>
    public int Port { get; set; } = 9600;

    /// <summary>超时（毫秒）</summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>采集器列表</summary>
    public FinsCollectorConfig[] Collectors { get; set; } = Array.Empty<FinsCollectorConfig>();
}

/// <summary>FINS 采集器配置项</summary>
public class FinsCollectorConfig
{
    /// <summary>采集器名称</summary>
    public string Name { get; set; } = "";

    /// <summary>起始地址</summary>
    public ushort StartAddress { get; set; }

    /// <summary>读取长度（字）</summary>
    public ushort Length { get; set; }
}
