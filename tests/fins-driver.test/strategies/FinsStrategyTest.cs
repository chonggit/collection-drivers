using fins.driver.strategies;

namespace fins.driver.test.strategies;

public class FinsStrategyTest
{
    // FinsStrategy requires a proper Machine with configuration.
    // Unit-internal method tests can be added when the class exposes
    // testable internal methods.
    [Fact]
    public void Strategy_TypeExists()
    {
        var type = typeof(FinsStrategy);
        Assert.NotNull(type);
    }
}
