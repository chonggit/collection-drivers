using CollectionDrivers.ScannerDriver.Strategies;

namespace CollectionDrivers.ScannerDriver.Test.Strategies;

public class ScannerStrategyTest
{
    [Fact]
    public void Strategy_TypeExists()
    {
        var type = typeof(ScannerStrategy);
        Assert.NotNull(type);
    }
}
