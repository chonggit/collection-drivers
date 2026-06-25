# Battery TCP Driver Design

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Build a TCP driver for battery formation (化成) cabinet data collection, following the architecture of the [fanuc-driver](https://github.com/Ladder99/fanuc-driver) base-driver framework. The driver collects real-time voltage/current data, alarms, and formation results from power cabinets via TCP (server mode), and delivers structured data to the host application through events and `System.Threading.Channels`.

**Target project location:** `D:/cihong/github/collection-drivers`

---

## 2. Project Structure

```
collection-drivers/
├── base-driver/                  ← 单独类库项目，代码直接复制
│   ├── base/
│   │   ├── Machine.cs
│   │   ├── Machines.cs
│   │   ├── Strategy.cs
│   │   ├── Handler.cs
│   │   ├── Transport.cs
│   │   ├── Veneer.cs
│   │   ├── Veneers.cs
│   │   └── Bootstrap.cs
│   └── base-driver.csproj
│
├── battery-driver/               ← 类库组件，无 Program.cs，被宿主引用
│   ├── BatteryMachine.cs         ← 继承 Machine
│   ├── BatteryHandler.cs         ← 数据管道分发
│   ├── connections/
│   │   └── TcpConnection.cs      ← TCP Server 模式，粘包处理
│   ├── strategies/
│   │   └── BatteryTcpStrategy.cs ← 适配被动接收模式
│   ├── collectors/
│   │   ├── ChannelData.cs        ← 336通道实时电压/电流
│   │   ├── EquipmentAlarm.cs     ← 机柜异常/报警
│   │   ├── CommandResult.cs      ← 化成结果 (OK/NG)
│   │   ├── WarningData.cs        ← 预警 (烟雾等)
│   │   └── CommandStatus.cs      ← 命令下发状态/ACK
│   ├── channels/
│   │   └── DataPublisher.cs      ← Channel + 事件对外接口
│   ├── models/
│   │   ├── ChannelRealData.cs    ← 数据结构定义
│   │   ├── AlarmData.cs
│   │   └── ResultData.cs
│   ├── BatteryDriverService.cs   ← 对外入口 (BackgroundService)
│   └── battery-driver.csproj
│
├── docs/
│   └── superpowers/specs/
├── examples/
│   ├── config.system.yml
│   ├── config.user.yml
│   └── config.machines.yml
└── collection-drivers.sln
```

### 2.1 Key Design Decisions

- **base-driver 复制使用**：base-driver 代码直接复制进来作为独立项目，不依赖 submodule 或 nuget。
- **battery-driver 为类库**：无 Program.cs，被宿主程序通过 `services.AddHostedService<BatteryDriverService>()` 启动。
- **单一解决方案**：两个项目在同一 .sln 中。

---

## 3. TCP Connection Management

### 3.1 Architecture

TcpConnection runs as a **TCP Server** (mirrors the existing `TcpServer` + `mySocket` pattern from the original wc_turn_jf project). The formation cabinets connect as clients and push data continuously.

```
TcpConnection
├── StartListeningAsync(port)       ← 启动 TcpListener
├── StopAsync()                      ← 停止监听，释放资源
├── SendAsync(byte[] data)           ← 向柜子发送命令
├── IsConnected                      ← 当前连接状态
│
├── 事件:
│   ├── OnClientConnected
│   ├── OnClientDisconnected
│   └── OnDataReceived (byte[])      ← 原始字节回调
│
├── 内部: ReceiveBuffer
│   ├── Append(byte[])               ← 追加新收到的字节
│   └── TryParse()                   ← 按帧头/帧尾拆包
│
└── 内部: SemaphoreSlim(1,1)         ← 发送互斥
```

### 3.2 Protocol Frame Format

The cabinets use a simple binary protocol with start/end markers:

| Start Byte | Packet Type | End Byte | Handling |
|---|---|---|---|
| `0xFD` | 实时数据（电压/电流） | `0xED` | ChannelData collector |
| `0xFE` | 报警/异常数据 | `0xEE` | EquipmentAlarm collector |
| `0xFF` | 命令回复/化成结果/状态 | `0xEF` | CommandResult / CommandStatus collectors |
| `0xEA` | 预警数据（烟雾等） | `0xED` | WarningData collector |

### 3.3 Sticky Packet Handling

The `ReceiveBuffer` accumulates raw bytes and splits frames by start/end markers. Logic mirrors the original `Fun_convert()` method:

```
Append(byte[] segment)
  → 合并到内部缓冲区
  → 循环尝试按起始字节拆包
     → 找到起始标记 → 读取固定长度 → 验证结束标记 → 产出完整帧
     → 丢弃无法识别的垃圾字节
  → 剩余未处理字节保留在缓冲区等待下次
```

### 3.4 Connection Lifecycle

- Accept new client → replace old connection (one active connection at a time, matching original behavior)
- No built-in reconnection (server waits for client)
- Heartbeat timeout: if no data received within configurable window, fire disconnect event

---

## 4. Strategy & Collectors

### 4.1 Data Flow

```
化成柜 → TCP → ReceiveBuffer 拆包 → 按帧头分发
                                         ↓
               ┌── 0xFD → ChannelData.cs  (336通道电压/电流)
               │── 0xFE → EquipmentAlarm.cs (异常报警)
               │── 0xFF → CommandResult.cs  (化成结果)
               │── 0xEA → WarningData.cs    (预警)
               │
               ↓
          结构化数据 → Veneers → Handler → DataPublisher
                                              ↓
                              ┌── Channel<ChannelRealData>
                              ├── event Action<...>
                              └── 宿主订阅消费
```

### 4.2 Strategy Adaptation

Unlike the original fanuc Strategy (active polling), BatteryTcpStrategy is **event-driven**:

```csharp
public class BatteryTcpStrategy : Strategy
{
    private readonly TcpConnection _connection;

    public override async Task InitializeAsync()
    {
        // 启动 TCP 监听，注册数据接收回调
        _connection.OnDataReceived += OnRawDataReceived;
        await _connection.StartListeningAsync(configPort);
    }

    private void OnRawDataReceived(byte[] raw)
    {
        // 按帧头分发到对应 Collector
        switch (raw[0])
        {
            case 0xFD: _channelDataCollector.Process(raw); break;
            case 0xFE: _alarmCollector.Process(raw); break;
            case 0xFF: _commandResultCollector.Process(raw); break;
            case 0xEA: _warningDataCollector.Process(raw); break;
        }
    }

    // SweepAsync 只做心跳检测 + 保活，不主动轮询
    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);
        CheckHeartbeat();
        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
```

### 4.3 Collectors

Each collector is responsible for parsing one frame type and producing structured data:

| Collector | Input | Output Data | Original Code Reference |
|---|---|---|---|
| `ChannelData` | `0xFD` frame (byte[]) | `ChannelRealData` (336 × voltage + current) | `myTurnRealData` |
| `EquipmentAlarm` | `0xFE` frame (byte[]) | `AlarmData` (channel flags) | `myTurnError` |
| `CommandResult` | `0xFF` frame (byte[]) | `ResultData` (OK/NG per channel) | `myTurnResult` |
| `WarningData` | `0xEA` frame (byte[]) | `WarningData` (smoke/fire alert) | `myTcpErrorRecive` |
| `CommandStatus` | `0xFF` frame (byte[]) | `StatusData` (cabinet status) | `Fun_turn_order` / `Fun_turn_ack` |

---

## 5. Data Models (Pure POCO)

Clean data structures with no external dependencies:

```csharp
public struct ChannelRealData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public float[] Voltage;      // 336 elements (7×48)
    public float[] Current;      // 336 elements (7×48)
    public DateTime Timestamp;
}

public struct AlarmData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] AbnormalFlags; // 336 channel alarm flags
    public DateTime Timestamp;
}

public struct ResultData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] ChannelResults; // 336 bytes: 0=none, 1=OK, 2=NG1, 3=NG2
    public DateTime Timestamp;
}

public struct StatusData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] ChannelStates;  // 7 layer states
    public DateTime Timestamp;
}

public struct WarningData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public WarningChannel[] Channels;
    public DateTime Timestamp;
}
```

---

## 6. Transport: DataPublisher (Zero External Dependencies)

### 6.1 Design

Use `System.Threading.Channels` (built into .NET) as internal buffer, and C# events as the primary external interface.

```csharp
public class DataPublisher : IDisposable
{
    // 按数据类型分发的独立 Channel
    private readonly Channel<ChannelRealData> _channelData = Channel.CreateBounded<ChannelRealData>(1000);
    private readonly Channel<AlarmData> _alarmData = Channel.CreateBounded<AlarmData>(500);
    private readonly Channel<ResultData> _resultData = Channel.CreateBounded<ResultData>(200);

    // 事件（宿主订阅）
    public event Action<ChannelRealData>? OnChannelData;
    public event Action<AlarmData>? OnAlarm;
    public event Action<ResultData>? OnResult;
    public event Action<StatusData>? OnStatus;
    public event Action<WarningData>? OnWarning;
    public event Action<Exception>? OnError;

    // 发布数据（由 Collector 调用）
    public void Publish(ChannelRealData data)
    {
        _channelData.Writer.TryWrite(data);
        OnChannelData?.Invoke(data);
    }
    // ... 类似 Publish(AlarmData), Publish(ResultData) 等
}
```

### 6.2 Why Channels + Events?

| Concern | Solution |
|---|---|
| 背压缓冲 | Channel 有界缓冲，避免快生产慢消费导致内存溢出 |
| 低延迟通知 | 事件同步触发，适合实时场景 |
| 批量处理 | 支持宿主用 `ReadAllAsync()` 批量消费 |
| 零外部依赖 | `System.Threading.Channels` 是 .NET 基础库的一部分 |

### 6.3 宿主使用两种方式

```csharp
// 方式 A：事件（低延迟、简单）
driver.OnChannelData += data => {
    db.InsertChannelData(data);
};

// 方式 B：Channel 批处理（高频场景）
var reader = driver.GetChannelReader<ChannelRealData>();
await foreach (var batch in reader.ReadAllAsync().Buffer(50))
{
    db.BulkInsert(batch);
}
```

---

## 7. BatteryDriverService — External Entry Point

```csharp
public class BatteryDriverService : BackgroundService
{
    // Events
    public event Action<ChannelRealData>? OnChannelData;
    public event Action<AlarmData>? OnAlarm;
    public event Action<ResultData>? OnResult;
    public event Action<StatusData>? OnStatus;
    public event Action<WarningData>? OnWarning;
    public event Action<Exception, string>? OnError;

    // Commands
    public Task StartFormationAsync(TurnOrder order);
    public Task PauseFormationAsync(byte cabinet, byte leftRight);
    public Task ResumeFormationAsync(byte cabinet, byte leftRight);
    public DriverStatus GetStatus();
}
```

Internal startup sequence:

```
BatteryDriverService.StartAsync(CancellationToken)
  → Bootstrap.Start(args)                   加载 YAML 配置
  → Machines.CreateMachines(config)         创建 Machine
  → TcpConnection = Machine[connection]     TCP Server 启动监听
  → Strategy.InitializeAsync()              注册数据回调
  → Sweep loop (heartbeat only)             进入后台扫描
```

---

## 8. YAML Configuration

Three-file structure matching fanuc-driver conventions:

### config.system.yml
```yaml
machine-base: &machine-base
  enabled: true
  type: battery.driver.BatteryMachine, battery-driver
  strategy: battery.driver.strategies.BatteryTcpStrategy, battery-driver
  handler: battery.driver.BatteryHandler, battery-driver
```

### config.user.yml
```yaml
source-1: &source-1
  battery.driver.BatteryMachine, battery-driver:
    sweep_ms: 1000
    net:
      port: 13000
      heartbeat_timeout_s: 60
```

### config.machines.yml
```yaml
machines:
  - id: cabinet_1
    <<: *machine-base
    <<: *source-1
    battery.driver.strategies.BatteryTcpStrategy, battery-driver:
      collectors:
      - battery.driver.collectors.ChannelData, battery-driver
      - battery.driver.collectors.EquipmentAlarm, battery-driver
      - battery.driver.collectors.CommandResult, battery-driver
```

---

## 9. Non-Goals (Out of Scope)

- **Data persistence** — Storing collected data is the host application's responsibility
- **UDP driver** — Will be added in a future phase
- **OPC UA / Omron Fins drivers** — Separate future drivers under the same collection-drivers solution
- **Configuration UI** — No GUI; YAML configuration only
- **MQTT / InfluxDB / SparkplugB transport** — Not needed for this deployment environment

---

## 10. Future Extensions

The architecture is designed to accommodate additional drivers:

```
collection-drivers/
├── base-driver/
├── battery-driver/            ← TCP (current)
├── battery-udp-driver/        ← UDP (future)
├── opcua-driver/              ← OPC UA (future)
└── fins-driver/               ← Omron Fins (future)
```

Each driver follows the same `Machine → Strategy → Collector → Handler` pattern with the common base-driver framework.

---

## 11. Migration Path from wc_turn_jf

| Original Component | New Location | Status |
|---|---|---|
| `TcpServer.cs` + `mySocket` | `connections/TcpConnection.cs` | Design confirmed |
| `myTurnInfo.cs` (business logic) → 数据模型 | `models/` + `collectors/` | Design confirmed |
| `myTcpErrorRecive.cs` → 预警处理 | `collectors/WarningData.cs` | Design confirmed |
| `turn_result` / `turn_abnormal` / `turn_realtimedata` | `models/` 对应结构 | Design confirmed |
| `turn_order` / `turn_order_ack` (命令下发) | `BatteryDriverService` 命令接口 | Design confirmed |
| Entity Framework + SQL Server (宿主层) | 宿主项目自行管理 | Out of scope |
