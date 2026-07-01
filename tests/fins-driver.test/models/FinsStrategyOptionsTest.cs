using CollectionDrivers.FinsDriver.Models;

namespace CollectionDrivers.FinsDriver.Test.Models;

public class FinsConfigTest
{
    [Fact]
    public void FinsConfig_Defaults()
    {
        var c = new FinsStrategyOptions();
        Assert.Equal("192.168.1.1", c.RemoteIp);
        Assert.Equal(9600, c.Port);
        Assert.Equal(5000, c.TimeoutMs);
        Assert.Empty(c.Collectors);
    }

    [Fact]
    public void FinsCollectorConfig_Defaults()
    {
        var c = new FinsCollectorConfig();
        Assert.Equal("", c.Name);
        Assert.Equal((ushort)0, c.StartAddress);
        Assert.Equal((ushort)0, c.Length);
    }
}
