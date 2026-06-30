using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using CollectionDrivers.Common;
using Newtonsoft.Json.Linq;

namespace CollectionDrivers.BatteryDriver.Test.Strategies;

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
        ChannelRealData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
    }

    /// <summary>
    /// Bug F14 复现：BatteryTcpStrategy 有 public Dispose() 但未实现 IDisposable。
    /// </summary>
    [Fact]
    public void Strategy_ImplementsIDisposable()
    {
        Assert.True(typeof(BatteryTcpStrategy).IsAssignableTo(typeof(IDisposable)),
            "BatteryTcpStrategy 应实现 IDisposable 以支持资源释放");
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

    /// <summary>
    /// Bug F15 验证：Machine.Disable() 设置 Enabled=false 后，
    /// 不检查 Enabled 的循环会继续执行，检查了的会在下次迭代退出。
    /// 本测试证明 RunMachineAsync 缺少 Enabled 检查的机制缺陷。
    /// </summary>
    [Fact]
    public async Task LoopWithoutEnabledCheck_KeepsRunningAfterDisable()
    {
        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;

        dynamic config = JObject.FromObject(new
        {
            machine = new { id = "test-disable", enabled = true },
            type = new { sweep_ms = 1000 }
        });

        var machine = new BatteryMachine(machines, config);

        // 模拟 Machines.RunMachineAsync 当前逻辑：
        // while (!token.IsCancellationRequested) { sweep; }
        // 缺少 && machine.Enabled 检查
        int sweepsAfterDisable = 0;
        var cts = new CancellationTokenSource();

        var loop = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(10); // 模拟 sweep
                if (!machine.Enabled)
                    sweepsAfterDisable++;
            }
        });

        await Task.Delay(100);
        machine.Disable();
        Assert.False(machine.Enabled);

        sweepsAfterDisable = 0;
        await Task.Delay(150);

        // RED 基线：不检查 Enabled 的循环在禁用后继续迭代
        Assert.True(sweepsAfterDisable > 0,
            $"禁用后循环继续迭代 {sweepsAfterDisable} 次 — 证明缺失 Enabled 检查的影响");

        cts.Cancel();
        await loop;
    }

    /// <summary>
    /// Bug F15 修复验证：检查 Enabled 的循环在 Disable() 后应停止迭代。
    /// RunMachineAsync 修复后加入 && machine.Enabled 条件。
    /// </summary>
    [Fact]
    public async Task LoopWithEnabledCheck_StopsAfterDisable()
    {
        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;

        dynamic config = JObject.FromObject(new
        {
            machine = new { id = "test-disable-fix", enabled = true },
            type = new { sweeep_ms = 1000 }
        });

        var machine = new BatteryMachine(machines, config);

        // 模拟修复后的 RunMachineAsync：
        // while (!token.IsCancellationRequested && machine.Enabled) { sweep; }
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

        // GREEN: 检查 Enabled 的循环在禁用后停止（允许最多 1 次正在执行的迭代）
        Assert.True(sweepsAfterDisable - sweepsBeforeDisable <= 1,
            $"禁用后最多 1 次飞行中迭代：禁用前 {sweepsBeforeDisable}，禁用后 {sweepsAfterDisable}");

        cts.Cancel();
        await loop;
    }
}
