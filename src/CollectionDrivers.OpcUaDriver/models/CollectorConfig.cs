namespace CollectionDrivers.OpcUaDriver.Models;

public class CollectorConfig
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "subscription";
    public int SamplingIntervalMs { get; set; } = 100;
    public int? SweepIntervalMs { get; set; }
    public NodeConfig[] Nodes { get; set; } = Array.Empty<NodeConfig>();
}
