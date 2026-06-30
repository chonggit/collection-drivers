using CollectionDrivers.FinsDriver.Strategies;

namespace CollectionDrivers.FinsDriver.Test.Strategies;

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

    /// <summary>
    /// Bug F14 复现：FinsStrategy 有 public Dispose() 但未实现 IDisposable。
    /// </summary>
    [Fact]
    public void Strategy_ImplementsIDisposable()
    {
        // RED: 当前返回 false
        // GREEN: 添加 : IDisposable 后返回 true
        Assert.True(typeof(FinsStrategy).IsAssignableTo(typeof(IDisposable)),
            "FinsStrategy 应实现 IDisposable 以支持资源释放");
    }
}
