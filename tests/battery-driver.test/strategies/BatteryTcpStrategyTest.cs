using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using CollectionDrivers.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

namespace CollectionDrivers.BatteryDriver.Test.Strategies;

public class BatteryTcpStrategyTest
{
    private static (Machine, BatteryTcpStrategy) CreateStrategy(
        string id = "test", int sweepMs = 100, BatteryTcpStrategyOptions? opts = null)
    {
        var logger = NullLogger<BatteryTcpStrategy>.Instance;
        var machine = new Machine((ILogger?)null);
        machine.Initialize(new MachineOptions { Id = id, Enabled = true, SweepMs = sweepMs });
        var options = opts ?? new BatteryTcpStrategyOptions();
        var strategy = new BatteryTcpStrategy(logger, machine, options);
        return (machine, strategy);
    }

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
        ChannelRealData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
    }

    [Fact]
    public void Strategy_ImplementsIDisposable()
    {
        Assert.True(typeof(BatteryTcpStrategy).IsAssignableTo(typeof(IDisposable)));
    }

    [Fact]
    public async Task SweepAsync_DoesNotThrow()
    {
        var (machine, strategy) = CreateStrategy();
        await strategy.SweepAsync(1);
    }

    [Fact]
    public async Task LoopWithoutEnabledCheck_KeepsRunningAfterDisable()
    {
        var (machine, _) = CreateStrategy("test-disable", 1000);

        int sweepsAfterDisable = 0;
        var cts = new CancellationTokenSource();

        var loop = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(10);
                if (!machine.Enabled) sweepsAfterDisable++;
            }
        });

        await Task.Delay(100);
        machine.Disable();
        Assert.False(machine.Enabled);

        sweepsAfterDisable = 0;
        await Task.Delay(150);

        Assert.True(sweepsAfterDisable > 0,
            $"禁用后循环继续迭代 {sweepsAfterDisable} 次");

        cts.Cancel();
        await loop;
    }

    [Fact]
    public async Task LoopWithEnabledCheck_StopsAfterDisable()
    {
        var (machine, _) = CreateStrategy("test-disable-fix", 1000);

        int sweepsAfterDisable = 0;
        var cts = new CancellationTokenSource();

        var loop = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && machine.Enabled)
            {
                await Task.Delay(10);
                sweepsAfterDisable++;
            }
        });

        await Task.Delay(100);
        machine.Disable();
        Assert.False(machine.Enabled);

        int sweepsBeforeDisable = sweepsAfterDisable;
        await Task.Delay(150);

        Assert.True(sweepsAfterDisable - sweepsBeforeDisable <= 1,
            $"禁用后最多 1 次飞行中迭代：禁用前 {sweepsBeforeDisable}，禁用后 {sweepsAfterDisable}");

        cts.Cancel();
        await loop;
    }

    [Fact]
    public void YamlDotNet_DefaultDeserialize_ProducesStringValues()
    {
        var yaml = @"
battery_tcp:
  port: 13000
  warning_port: 13100
  heartbeat_timeout_s: 60
";
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
        var config = deserializer.Deserialize<object>(yaml);

        var root = Assert.IsType<Dictionary<object, object>>(config);
        var strategy = Assert.IsType<Dictionary<object, object>>(root["battery_tcp"]);

        Assert.Equal(typeof(string), strategy["port"].GetType());
        Assert.Throws<InvalidCastException>(() => { var _ = (int)strategy["port"]; });

        int port = Convert.ToInt32(strategy["port"]);
        Assert.Equal(13000, port);
    }

    [Fact]
    public void CastInt_OnBoxedString_ThrowsInvalidCastException()
    {
        object boxedString = "13000";
        Assert.Throws<InvalidCastException>(() => { var _ = (int)boxedString; });
        Assert.Equal(13000, Convert.ToInt32(boxedString));
    }

    [Fact]
    public void CastInt_OnBoxedLong_ThrowsInvalidCastException()
    {
        object boxedLong = 13000L;
        Assert.Throws<InvalidCastException>(() => { var _ = (int)boxedLong; });
        Assert.Equal(13000, Convert.ToInt32(boxedLong));
    }

    [Fact]
    public void CastInt_OnBoxedInt_Succeeds()
    {
        object boxedInt = 13000;
        Assert.Equal(13000, (int)boxedInt);
    }

    [Fact]
    public void BaseSweepAsync_IsAbstract()
    {
        Assert.True(typeof(Strategy).IsAbstract);
        Assert.True(typeof(Strategy).GetMethod("SweepAsync")!.IsAbstract);
        Assert.True(typeof(BatteryTcpStrategy).GetMethod("SweepAsync")!.DeclaringType == typeof(BatteryTcpStrategy));
    }

    [Fact]
    public void Strategy_OnError_HasNoSubscribers_WhenCreatedDirectly()
    {
        var onErrorField = typeof(Strategy).GetField("OnError",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
        Assert.NotNull(onErrorField);

        var (_, strategy) = CreateStrategy("test-onerror");
        Assert.Null(onErrorField!.GetValue(strategy));
    }

    [Fact]
    public async Task Strategy_OnError_HasSubscribers_AfterMachineScopeSetup()
    {
        var onErrorField = typeof(Strategy).GetField("OnError",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);
        Assert.NotNull(onErrorField);

        var (machine, strategy) = CreateStrategy("test-onerror-subscribed");

        // 模拟 Machine.AddStrategyAsync 的 OnError 订阅
        strategy.OnError += (ex, ctx) => { };

        Assert.NotNull(onErrorField!.GetValue(strategy));

        var raiseMethod = typeof(Strategy).GetMethod("RaiseOnError",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(raiseMethod);

        var exception = Record.Exception(() =>
        {
            raiseMethod!.Invoke(strategy, new object[] { new InvalidOperationException("test error"), "test context" });
        });
        Assert.Null(exception);
    }
}
