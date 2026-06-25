using scanner.driver.strategies;

namespace scanner.driver.test.strategies;

public class ScannerStrategyTest
{
    [Fact]
    public void Strategy_TypeExists()
    {
        var type = typeof(ScannerStrategy);
        Assert.NotNull(type);
    }
}
