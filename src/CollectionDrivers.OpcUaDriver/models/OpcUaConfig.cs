namespace CollectionDrivers.OpcUaDriver.Models;

public class OpcUaConfig
{
    public string Endpoint { get; set; } = "";
    public bool UseSecurity { get; set; } = false;
    public int ReconnectPeriodMs { get; set; } = 10000;
    public bool AutoAcceptCerts { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public CollectorConfig[] Collectors { get; set; } = Array.Empty<CollectorConfig>();
}
