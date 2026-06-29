using CollectionDrivers.OpcUaDriver.Models;

namespace CollectionDrivers.OpcUaDriver.Test.Models;

public class OpcUaConfigTest
{
    [Fact]
    public void NodeConfig_Defaults()
    {
        var n = new NodeConfig();
        Assert.Equal("", n.Id);
        Assert.Null(n.Alias);
    }

    [Fact]
    public void CollectorConfig_Defaults()
    {
        var c = new CollectorConfig();
        Assert.Equal("subscription", c.Mode);
        Assert.Equal(100, c.SamplingIntervalMs);
        Assert.Empty(c.Nodes);
    }

    [Fact]
    public void OpcUaConfig_Defaults()
    {
        var c = new OpcUaConfig();
        Assert.Equal("", c.Endpoint);
        Assert.False(c.UseSecurity);
        Assert.Empty(c.Collectors);
    }
}
