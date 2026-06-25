using fins.driver.models;

namespace fins.driver.test.models;

public class FinsConfigTest
{
    [Fact]
    public void FinsConfig_Defaults()
    {
        var c = new FinsConfig();
        Assert.Equal("", c.RemoteIp);
        Assert.Equal(9600, c.Port);
        Assert.Equal(2000, c.TimeoutMs);
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
