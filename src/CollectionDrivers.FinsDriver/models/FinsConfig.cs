namespace CollectionDrivers.FinsDriver.Models;

public class FinsCollectorConfig
{
    public string Name { get; set; } = "";
    public ushort StartAddress { get; set; }
    public ushort Length { get; set; }
}

public class FinsConfig
{
    public string RemoteIp { get; set; } = "";
    public int Port { get; set; } = 9600;
    public int TimeoutMs { get; set; } = 2000;
    public FinsCollectorConfig[] Collectors { get; set; } = Array.Empty<FinsCollectorConfig>();
}
