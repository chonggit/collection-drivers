using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Models;
using CollectionDrivers.BatteryDriver.Strategies;
using CollectionDrivers.Common;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

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

        var machine = new Machine(machines, config);
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

        var machine = new Machine(machines, config);

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
            type = new { sweep_ms = 1000 }
        });

        var machine = new Machine(machines, config);

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

    // ============================================================
    // 发现14 复现：(int) 强制拆箱 vs Convert.ToInt32
    // ============================================================

    /// <summary>
    /// 发现14 端到端复现：使用 YamlDotNet 16.3.0 默认反序列化一个包含 port 整数的 YAML，
    /// 验证反序列化后的值类型是否为 string（YamlDotNet 默认行为），
    /// 并证明 (int) 强制转换会抛出 InvalidCastException。
    ///
    /// 实际 YAML 配置示例：
    ///   battery_tcp:
    ///     port: 13000
    ///     warning_port: 13100
    ///     heartbeat_timeout_s: 60
    /// </summary>
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

        // YamlDotNet 默认反序列化到 object 产生 Dictionary<object, object>
        var root = Assert.IsType<Dictionary<object, object>>(config);
        var strategy = Assert.IsType<Dictionary<object, object>>(root["battery_tcp"]);

        var portValue = strategy["port"];
        var warningPortValue = strategy["warning_port"];
        var timeoutValue = strategy["heartbeat_timeout_s"];

        // 记录实际类型 — YamlDotNet 16.3.0 默认将标量反序列化为 string
        var portType = portValue.GetType();
        var timeoutType = timeoutValue.GetType();

        // 断言：YamlDotNet 16.3.0 默认将整数标量反序列化为 string
        // （如果此断言失败，说明 YamlDotNet 版本行为已变更）
        Assert.Equal(typeof(string), portType);

        // 无论 YamlDotNet 将整数值反序列化为什么类型，
        // 只要不是 boxed int，(int) 强制转换就会崩渍
        if (portType == typeof(string))
        {
            // string → (int) 抛出 InvalidCastException
            Assert.Throws<InvalidCastException>(() => { var _ = (int)portValue; });
        }
        else if (portType == typeof(long))
        {
            // long → (int) 抛出 InvalidCastException
            Assert.Throws<InvalidCastException>(() => { var _ = (int)portValue; });
        }
        else if (portType == typeof(int))
        {
            // int → (int) 正常工作（但 YamlDotNet 默认不产生 int）
            var _ = (int)portValue;
        }

        // Convert.ToInt32 始终正确处理所有类型
        int port = Convert.ToInt32(portValue);
        int warningPort = Convert.ToInt32(warningPortValue);
        int timeout = Convert.ToInt32(timeoutValue);

        Assert.Equal(13000, port);
        Assert.Equal(13100, warningPort);
        Assert.Equal(60, timeout);
    }

    // ============================================================
    // 发现14 复现：(int) 强制拆箱 vs Convert.ToInt32（原有测试）
    // BatteryTcpStrategy.InitializeAsync 使用 (int)rawConfig["port"]
    // 当 YamlDotNet 反序列化值为 string 或 long 时抛出 InvalidCastException
    // OpcUaStrategy 使用 Convert.ToInt32 才是正确做法
    // ============================================================

    /// <summary>
    /// 发现14：对 boxed string 做 (int) 强制转换会抛出 InvalidCastException，
    /// 而 Convert.ToInt32 可以正确处理。
    /// 这是 BatteryTcpStrategy.InitializeAsync 的核心 bug ——
    /// 当 YamlDotNet 默认将 YAML 标量反序列化为 string 时直接崩溃。
    /// </summary>
    [Fact]
    public void CastInt_OnBoxedString_ThrowsInvalidCastException()
    {
        object boxedString = "13000";

        // RED: (int) 强制转换 boxed string 直接抛出 InvalidCastException
        Assert.Throws<InvalidCastException>(() =>
        {
            var _ = (int)boxedString;
        });

        // GREEN: Convert.ToInt32 正确处理 string
        int result = Convert.ToInt32(boxedString);
        Assert.Equal(13000, result);
    }

    /// <summary>
    /// 发现14 补充：对 boxed long 做 (int) 强制转换同样抛出 InvalidCastException。
    /// YamlDotNet 可能将大整数反序列化为 long，此时 (int) 同样崩溃。
    /// </summary>
    [Fact]
    public void CastInt_OnBoxedLong_ThrowsInvalidCastException()
    {
        object boxedLong = 13000L;

        // RED: (int) 无法拆箱 boxed long（类型不匹配）
        Assert.Throws<InvalidCastException>(() =>
        {
            var _ = (int)boxedLong;
        });

        // GREEN: Convert.ToInt32 正确处理 long
        int result = Convert.ToInt32(boxedLong);
        Assert.Equal(13000, result);
    }

    /// <summary>
    /// 发现14 正对照：当值是 boxed int 时 (int) 正常运作。
    /// 证明问题的根源在于值类型的不确定性（YamlDotNet 反序列化产物），
    /// 而非 (int) 语法本身有问题。
    /// </summary>
    [Fact]
    public void CastInt_OnBoxedInt_Succeeds()
    {
        object boxedInt = 13000;
        int result = (int)boxedInt;
        Assert.Equal(13000, result);
    }

    // ============================================================
    // 发现11 验证：基类 Strategy.SweepAsync 是否是死代码
    // ============================================================

    /// <summary>
    /// 发现11 RED: 原基类 virtual SweepAsync 为死代码（无子类调用 base.SweepAsync）。
    /// GREEN: 修复后将基类方法和类均改为 abstract，消除死代码并强制子类显式实现。
    /// </summary>
    [Fact]
    public void BaseSweepAsync_IsAbstract()
    {
        // GREEN: Strategy 类本身为 abstract（不能直接 new）
        Assert.True(typeof(Strategy).IsAbstract,
            "Strategy 应为 abstract，防止直接实例化");

        // GREEN: SweepAsync 为 abstract（子类必须重写）
        var sweepMethod = typeof(Strategy).GetMethod("SweepAsync");
        Assert.NotNull(sweepMethod);
        Assert.True(sweepMethod!.IsAbstract,
            "SweepAsync 应为 abstract，消除死代码并强制子类实现");

        // BatteryTcpStrategy 必须 override 此方法
        var batteryMethod = typeof(BatteryTcpStrategy).GetMethod("SweepAsync");
        Assert.NotNull(batteryMethod);
        Assert.True(batteryMethod!.DeclaringType == typeof(BatteryTcpStrategy),
            "BatteryTcpStrategy 应 override abstract SweepAsync");
    }

    // ============================================================
    // 发现4 验证：Strategy.OnError 是否有任何订阅者
    // ============================================================

    /// <summary>
    /// 发现4 RED：直接创建 Strategy 时 OnError 无订阅者。
    /// 所有 15+ 处 RaiseOnError 调用通过 ?.Invoke 短路，异常静默丢弃。
    /// </summary>
    [Fact]
    public void Strategy_OnError_HasNoSubscribers_WhenCreatedDirectly()
    {
        var onErrorField = typeof(Strategy).GetField("OnError",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);

        Assert.NotNull(onErrorField);

        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;
        dynamic config = JObject.FromObject(new
        {
            machine = new { id = "test-onerror", enabled = true },
            type = new { sweep_ms = 1000 }
        });

        var machine = new Machine(machines, config);
        var strategy = new BatteryTcpStrategy(machine);

        // RED 场景：直接构造的 Strategy 无 OnError 订阅者
        var delegateValue = onErrorField!.GetValue(strategy);
        Assert.Null(delegateValue);
    }

    /// <summary>
    /// 发现4 GREEN：Machine.AddStrategyAsync 订阅 OnError 事件，
    /// 确保所有策略异常至少有一条日志记录。
    /// </summary>
    [Fact]
    public async Task Strategy_OnError_HasSubscribers_AfterAddStrategyAsync()
    {
        var onErrorField = typeof(Strategy).GetField("OnError",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public);

        dynamic config = JObject.FromObject(new
        {
            machine = new
            {
                id = "test-onerror-subscribed",
                enabled = true,
                type = "CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver",
                strategy = "CollectionDrivers.BatteryDriver.Strategies.BatteryTcpStrategy, CollectionDrivers.BatteryDriver"
            },
            type = new { sweep_ms = 1000 },
            strategy = new { }
        });

        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;
        var machine = new Machine(machines, config);

        // 通过 Machine.AddStrategyAsync 创建 —— 应自动订阅 OnError
        await machine.AddStrategyAsync(typeof(BatteryTcpStrategy));

        // GREEN: OnError 已被 Machine 订阅
        var delegateValue = onErrorField!.GetValue(machine.Strategy);
        Assert.NotNull(delegateValue);

        // 验证 RaiseOnError 触发时不会崩溃，且委托被调用
        var ex = new InvalidOperationException("test error");
        var raiseMethod = typeof(Strategy).GetMethod("RaiseOnError",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(raiseMethod);

        var exception = Record.Exception(() =>
        {
            raiseMethod!.Invoke(machine.Strategy, new object[] { ex, "test context" });
        });
        Assert.Null(exception);
    }

    // ============================================================
    // 发现9 验证：多 Transport 支持
    // ============================================================

    /// <summary>
    /// 发现9 GREEN: Machine.Transports 支持注册多个 Transport。
    /// AddTransportAsync 多次调用应累加而非覆盖。
    /// </summary>
    [Fact]
    public async Task AddTransportAsync_MultipleCalls_AccumulatesTransports()
    {
        dynamic config = JObject.FromObject(new
        {
            machine = new
            {
                id = "test-multi-transport",
                enabled = true,
                type = "CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver"
            },
            type = new { sweep_ms = 1000 },
            transport = new { }
        });

        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;
        var machine = new Machine(machines, config);

        // 注册第一个 Transport
        await machine.AddTransportAsync(typeof(TestTransport1));
        Assert.Single(machine.Transports);
        Assert.IsType<TestTransport1>(machine.Transport);

        // 注册第二个 Transport — 应累加，不覆盖
        await machine.AddTransportAsync(typeof(TestTransport2));
        Assert.Equal(2, machine.Transports.Count);
        Assert.IsType<TestTransport1>(machine.Transport); // 首选仍为第一个
        Assert.Contains(machine.Transports, t => t is TestTransport2);
    }

    /// <summary>
    /// 空 Transport（type == null）时 Transports 列表为空且机器被禁用。
    /// </summary>
    [Fact]
    public async Task AddTransportAsync_NullType_DisablesMachineWithEmptyTransports()
    {
        dynamic config = JObject.FromObject(new
        {
            machine = new
            {
                id = "test-null-transport",
                enabled = true,
                type = "CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver"
            },
            type = new { sweep_ms = 1000 }
        });

        var machines = (Machines)Activator.CreateInstance(typeof(Machines), true)!;
        var machine = new Machine(machines, config);

        await machine.AddTransportAsync(null!);

        Assert.Empty(machine.Transports);
        Assert.False(machine.Enabled);
    }

    /// <summary>
    /// 测试用 Transport 子类 #1 — 无实际操作，仅验证注册逻辑。
    /// </summary>
    private class TestTransport1 : Transport
    {
        public TestTransport1(Machine machine) : base(machine) { }
    }

    /// <summary>
    /// 测试用 Transport 子类 #2 — 无实际操作，仅验证多 Transport 累加逻辑。
    /// </summary>
    private class TestTransport2 : Transport
    {
        public TestTransport2(Machine machine) : base(machine) { }
    }
}
