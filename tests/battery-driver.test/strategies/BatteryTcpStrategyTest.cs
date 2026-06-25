using battery.driver.collectors;
using battery.driver.strategies;
using l99.driver.@base;
using Newtonsoft.Json.Linq;

namespace battery.driver.test.strategies;

public class BatteryTcpStrategyTest
{
    [Fact]
    public void ChannelDataCollector_Parses_0xFD_Frame()
    {
        var frame = new byte[2696];
        frame[0] = 0xFD;
        frame[5] = 0x01;
        frame[6] = 0x01;
        BitConverter.GetBytes(3.7f).CopyTo(frame, 7);
        frame[2695] = 0xED;

        var collector = new ChannelData();
        battery.driver.models.ChannelRealData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
    }

    [Fact]
    public async Task SweepAsync_DoesNotThrow()
    {
        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;

        dynamic config = JObject.FromObject(new
        {
            machine = new { id = "test", enabled = true },
            type = new { sweep_ms = 1000 }
        });

        var machine = new BatteryMachine(machines, config);
        var strategy = new BatteryTcpStrategy(machine);

        await strategy.SweepAsync(1);
    }
}
