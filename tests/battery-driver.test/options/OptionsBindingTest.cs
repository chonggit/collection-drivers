using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.Common;
using Microsoft.Extensions.Configuration;

namespace battery_driver.test.Options;

/// <summary>
/// 验证 Configuration DTO 的 IConfiguration.Bind() 行为。
/// TDD 跳过理由：纯 DTO 绑定行为验证，属配置测试非业务逻辑。
/// </summary>
public class OptionsBindingTest
{
    [Fact]
    public void BatteryTcpOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Port"] = "12345",
                ["WarningPort"] = "12346",
                ["HeartbeatTimeoutS"] = "120"
            }).Build();

        var options = new BatteryTcpStrategyOptions();
        config.Bind(options);

        Assert.Equal(12345, options.Port);
        Assert.Equal(12346, options.WarningPort);
        Assert.Equal(120, options.HeartbeatTimeoutS);
    }

    [Fact]
    public void BatteryTcpOptions_HasCorrectDefaults()
    {
        var options = new BatteryTcpStrategyOptions();

        Assert.Equal(13000, options.Port);
        Assert.Equal(13100, options.WarningPort);
        Assert.Equal(60, options.HeartbeatTimeoutS);
    }

    [Fact]
    public void MachineOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Machines:0:Id"] = "test-machine",
                ["Machines:0:Enabled"] = "true",
                ["Machines:0:DriverId"] = "Battery",
                ["Machines:0:SweepMs"] = "3000"
            }).Build();

        var options = new CollectionDriverOptions();
        config.Bind(options);

        Assert.Single(options.Machines);
        Assert.Equal("test-machine", options.Machines[0].Id);
        Assert.True(options.Machines[0].Enabled);
        Assert.Equal("Battery", options.Machines[0].DriverId);
        Assert.Equal(3000, options.Machines[0].SweepMs);
    }
}
