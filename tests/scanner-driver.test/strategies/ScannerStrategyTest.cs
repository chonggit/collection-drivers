using CollectionDrivers.Common;
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

    /// <summary>
    /// Bug F14 复现：ScannerStrategy 有 public Dispose() 但未实现 IDisposable，
    /// 导致 using 块和 DI 容器无法自动释放资源。
    /// </summary>
    [Fact]
    public void Strategy_ImplementsIDisposable()
    {
        // RED: 当前返回 false
        // GREEN: 添加 : IDisposable 后返回 true
        Assert.True(typeof(ScannerStrategy).IsAssignableTo(typeof(IDisposable)),
            "ScannerStrategy 应实现 IDisposable 以支持资源释放");
    }

    /// <summary>
    /// Bug #13 复现：async 模式下 SweepAsync 提前 return，从未设置 LastSuccess/IsHealthy。
    /// bool 默认 false，导致 TransportHandler 始终上报 online=false, healthy=false。
    /// 修复后 async 模式应正确反映运行状态。
    /// </summary>
    [Fact]
    public async Task SweepAsync_AsyncMode_SetsLastSuccessAndIsHealthyToTrue()
    {
        // 跨程序集 dynamic 绑定方案：
        // - 顶层/config.machine 用 ExpandoObject（dot-notation：.machine.enabled）
        // - type/strategy 用 Dictionary（indexer：["sweep_ms"] + IDictionary cast）
        dynamic config = new System.Dynamic.ExpandoObject();
        dynamic machineSection = new System.Dynamic.ExpandoObject();
        machineSection.enabled = true;
        machineSection.id = "test-async-scanner";
        config.machine = machineSection;
        config.type = new Dictionary<string, object> { ["sweep_ms"] = 10 };
        config.strategy = new Dictionary<string, object>
        {
            ["mode"] = "async",
            ["host"] = "127.0.0.1",
            ["port"] = 19999
        };

#pragma warning disable CS8625
        var machine = new ScannerMachine(null!, (object)config);
#pragma warning restore CS8625

        var strategy = new ScannerStrategy(machine);

        // async 模式：SweepAsync 应快速返回并上报健康状态
        await strategy.SweepAsync(1);

        // RED: 当前代码 LastSuccess == false, IsHealthy == false
        // GREEN: 修复后两者均为 true
        Assert.True(strategy.LastSuccess,
            "async 模式 sweep 未失败，LastSuccess 应为 true");
        Assert.True(strategy.IsHealthy,
            "async 模式 sweep 未失败，IsHealthy 应为 true");
    }
}
