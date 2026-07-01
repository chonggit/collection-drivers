namespace CollectionDrivers.OpcUaDriver.Models;

/// <summary>
/// OPC UA 策略配置。
/// </summary>
public class OpcUaStrategyOptions
{
    /// <summary>OPC UA 端点 URL</summary>
    public string Endpoint { get; set; } = "opc.tcp://localhost:4840";

    /// <summary>是否启用安全连接</summary>
    public bool UseSecurity { get; set; }

    /// <summary>重连周期（毫秒）</summary>
    public int ReconnectPeriodMs { get; set; } = 10000;

    /// <summary>是否自动接受证书</summary>
    public bool AutoAcceptCerts { get; set; } = true;

    /// <summary>用户名（可选）</summary>
    public string? UserName { get; set; }

    /// <summary>密码（可选）</summary>
    public string? Password { get; set; }

    /// <summary>采集器列表</summary>
    public OpcUaCollectorConfig[] Collectors { get; set; } = Array.Empty<OpcUaCollectorConfig>();
}

/// <summary>OPC UA 采集器配置项</summary>
public class OpcUaCollectorConfig
{
    /// <summary>采集器名称</summary>
    public string Name { get; set; } = "";

    /// <summary>采集模式：subscription 或 poll</summary>
    public string Mode { get; set; } = "subscription";

    /// <summary>采样间隔（毫秒）</summary>
    public int SamplingIntervalMs { get; set; } = 100;

    /// <summary>轮询间隔（毫秒），仅 poll 模式</summary>
    public int? SweepIntervalMs { get; set; }

    /// <summary>节点列表</summary>
    public OpcUaNodeConfig[] Nodes { get; set; } = Array.Empty<OpcUaNodeConfig>();
}

/// <summary>OPC UA 节点配置</summary>
public class OpcUaNodeConfig
{
    /// <summary>节点 ID</summary>
    public string Id { get; set; } = "";

    /// <summary>别名</summary>
    public string? Alias { get; set; }
}
