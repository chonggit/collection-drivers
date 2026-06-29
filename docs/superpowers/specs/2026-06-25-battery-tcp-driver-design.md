# Battery TCP Driver Design

**Date:** 2026-06-25
**Project:** collection-drivers
**Status:** Draft

---

## 1. Overview

Build a TCP driver for battery formation (化成) cabinet data collection, following the architecture of the [fanuc-driver](https://github.com/Ladder99/fanuc-driver) base-driver framework (main branch latest). The driver collects real-time voltage/current data, alarms, and formation results from power cabinets via TCP (server mode), and delivers structured data to the host application through events and `System.Threading.Channels`.

**Target project location:** `D:/cihong/github/collection-drivers`

**base-driver 来源**：fanuc-driver main 分支最新代码，直接复制到本仓库作为独立项目（非 submodule / nuget）。`OnStrategySweepCompleteInternalAsync` 为本实现需要在 Handler 基类中新增的方法。

**机柜架构**：支持多机柜，物理上每个实例一个 TCP Listener（单端口），帧内通过 `CabinetIndex (1-20)` + `LeftRight (1-2)` 标识数据来源。运行时可启动多个实例分别监听不同机柜组。

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
│   ├── BatteryHandler.cs         ← 数据管道分发（Veneers → DataPublisher）
│   ├── connections/
│   │   └── TcpConnection.cs      ← TCP Server 模式，粘包处理
│   ├── strategies/
│   │   └── BatteryTcpStrategy.cs ← 适配被动接收模式
│   ├── collectors/
│   │   ├── ChannelData.cs        ← 336通道实时电压/电流
│   │   ├── EquipmentAlarm.cs     ← 机柜异常/报警
│   │   ├── CommandResult.cs      ← 化成结果 (OK/NG)
│   │   ├── WarningData.cs        ← 预警 (烟雾等)
│   │   └── CommandStatus.cs      ← 命令下发状态/ACK + 柜体状态
│   ├── channels/
│   │   └── DataPublisher.cs      ← Channel + 事件对外接口
│   ├── models/
│   │   ├── ChannelRealData.cs    ← 数据结构定义
│   │   ├── AlarmData.cs
│   │   ├── ResultData.cs
│   │   ├── StatusData.cs
│   │   ├── AckData.cs
│   │   └── WarningData.cs
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
- **Veneers 管道绕过**：battery-driver 中 Collector 直接调用 `Handler.Publish()`，不经过 base-driver 的 Veneers 管道（Veneers 保留在项目结构中兼容框架但不启用）。
- **多机柜支持**：帧内以 `CabinetIndex` + `LeftRight` 标识数据来源。单实例管理一个 TCP Listener（单端口 13000），多机柜场景下启动多个实例（端口可配置）。

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
│   ├── TryParse()                   ← 按帧头/帧尾拆包
│   └── MaxBufferSize                ← 容量上限（默认 65536）
│
└── 内部: SemaphoreSlim(1,1)         ← 发送互斥
```

### 3.2 Protocol Frame Format

The cabinets use a simple binary protocol with start/end markers. Each frame type has a fixed total length, defined by the original `mCount()` / `mPkgLen()` methods:

| Start | Type | Total (bytes) | Payload Layout | End | Collector |
|---|---|---|---|---|---|
| `0xFD` | 实时数据(电压/电流) | **2696** | begin(1) + len(2) + seq(2) + cabinet(1) + lr(1) + voltage 336×float(1344) + current 336×float(1344) + end(1) | `0xED` | `ChannelData` |
| `0xFE` | 异常报警 | **344** | begin(1) + len(2) + seq(2) + cabinet(1) + lr(1) + abnormal 336×byte(336) + end(1) | `0xEE` | `EquipmentAlarm` |
| `0xFF` | 化成结果 | **344** | begin(1) + len(2) + seq(2) + cabinet(1) + lr(1) + results 336×byte(336) + end(1) | `0xEF` | `CommandResult` |
| `0xFF` | 命令 ACK | **7** | begin(1) + len(2) + seq(2) + ack_status(1) + end(1) | `0xEF` | `CommandStatus` (ProcessAck) |
| `0xFF` | 柜体状态 | **65** | begin(1) + len(2) + seq(2) + cabinet(1) + lr(1) + technology(50) + layer_states 7×byte(7) + end(1) | `0xEF` | `CommandStatus` (ProcessState) |
| `0xEA` | 预警数据(烟雾等) | **155** | begin(1) + len(2) + seq(2) + cabinet(1) + lr(1) + 7×WarningSub(147) + end(1) | `0xED` | `WarningData` |

> **关于 `m_len` 字段**：帧的第 2-3 字节为长度字段（大端 ushort），其值为**去掉起始和结束字节**后的长度，即 `total_length - 2`。

> **technology 字段编码**：柜体状态帧和命令下发帧中的 50 字节 technology 字段使用 **UTF-8 编码**（与原项目 `Encoding.UTF8.GetBytes(technology)` 一致）。不足 50 字节时右补 `0x00`，超出 50 字节时截断（触发 `OnError` 事件通知宿主）。反序列化时跳过尾随 `0x00` 字节后使用 UTF-8 解码。

**WarningSub 字段布局**（每层 21 字节 = 1 + 4 + 4 + 4 + 4 + 4，共 7 层）：
```
byte   Layer;          // 层号 (1-7)
float  Voltage;        // 当前电压
float  Current;        // 当前电流
float  VoltageBefore;  // 参考电压（异常前）
float  CurrentBefore;  // 参考电流（异常前）
int    ChannelIndex;   // 通道索引（协议原始值 = layer * 48 + pos, layer 1-7, 结果范围 48-383）
```

> **ChannelIndex 说明**：协议原始值使用 **1-based 层号**计算（`layer * 48 + pos`），但驱动内部数组索引使用 **0-based**（`(layer - 1) * 48 + pos`，范围 0-335，见 §5 索引公式）。两种计算结果相差 48，使用时需注意转换。

**0xFE 异常值含义**：
```
0=无报警  1=其他报警  2=过压  3=欠压  4=过流  5=欠流  6=烟感报警  7=采样线掉线
```

**0xFF 化成结果值含义**：
```
1=OK(化成成功)  2=NG1(电压/容量不合格)  3=NG2(电池检测问题)
```

**0xFF 子分发规则**（按帧总长度区分，匹配优先级从短到长）：
| 长度 | 类型 | Collector 方法 |
|---|---|---|
| 7 字节 | 命令 ACK（`m_ack` 字节：1=成功, 0=失败） | `CommandStatus.ProcessAck()` |
| 65 字节 | 柜体状态（7 层运行状态 + 工艺参数字段） | `CommandStatus.ProcessState()` |
| 344 字节 | 化成结果（336 通道 OK/NG） | `CommandResult.Process()` |

### 3.3 Sticky Packet Handling

The `ReceiveBuffer` accumulates raw bytes and splits frames by start/end markers. Logic mirrors the original `Fun_convert()` method:

```
Append(byte[] segment)
  → 合并到内部缓冲区（buffer 为累积字节数组）
  → len = buffer.Length
  → 如果 len < 7，直接返回（最小帧长是 7 字节 ACK）

  → 按起始字节依次尝试四个子解析器（原项目顺序）:
     ① 0xFF 子解析器
        检查 buffer[0] == 0xFF
        → 按长度从小到大依次尝试（匹配后 return，不继续尝试更长类型）:
            len >= 7   → ACK 拆包，验证 end==0xEF ✓ → 产出完整 ACK 帧
            len >= 65  → 柜体状态拆包，验证 end==0xEF ✓ → 产出完整状态帧
            len >= 344 → 化成结果拆包，验证 end==0xEF ✓ → 产出完整结果帧

     ② 0xFE 子解析器
        检查 buffer[0] == 0xFE
        → len >= 344 → 拆包，验证 end==0xEE ✓ → 产出完整异常帧

     ③ 0xFD 子解析器
        检查 buffer[0] == 0xFD
        → len >= 2696 → 拆包，验证 end==0xED ✓ → 产出完整实时数据帧

     ④ 0xEA 子解析器（预警/烟雾数据，独立端口 13100 接收）
        检查 buffer[0] == 0xEA
        → len >= 155 → 拆包，验证 end==0xED ✓ → 产出完整预警帧

  → 四种解析器均不匹配时，丢弃 buffer[0]，前进一个字节重试垃圾回收
  → 剩余未处理字节保留在缓冲区等待下次 Append
```

> **注意**：0xEA 预警数据在原项目中通过**独立 TCP 端口 13100** 接收（`TcpErrorRecive.cs`），使用独立的 `TcpConnection` 实例和 `ReceiveBuffer`。本设计保持此架构：driver 可配置两个端口（默认 13000 数据端口，13100 预警端口），各自独立管理连接生命周期和心跳。

**ReceiveBuffer 与 Strategy 的分层验证**：
- `ReceiveBuffer.TryParse()` 使用 `>=` 长度检查 + 结束字节验证，保证拆出的帧是完整且 end 标记正确的。
- Strategy 收到的帧已经是经过验证的完整帧，因此在 `Dispatch0xFF()` 中使用 `switch(raw.Length)` 精确匹配各帧类型，无需重复验证 end 字节。
- 验证通过后还会检查帧内 `m_len` 字段是否与总长度 `-2` 一致，不一致时触发 `OnError` 事件并丢弃该帧。

**ReceiveBuffer 容量保护**：
- 设定 `MaxBufferSize`（建议 65536），当累积字节超过上限时丢弃最旧数据或断开连接，防止异常客户端导致 OOM。
- 在 `Append()` 入口检查 `buffer.Length > MaxBufferSize`。

### 3.4 Connection Lifecycle

- Accept new client → close old connection (one active connection at a time, matching original behavior)
- No built-in reconnection (server waits for client)
- Heartbeat: if no data received within configurable window (`heartbeat_timeout_s`), fire disconnect event
- 0xEA 预警端口 13100 作为独立连接管理，独立于 13000 端口的数据连接

**连接替换的线程安全**（原项目竞态修复）：
```
AcceptClientConnections()
  → 旧连接使用 CancellationTokenSource.Cancel() 通知停止
  → 等待旧接收任务退出（await Task.WhenAny 带超时）
  → 关闭旧 TcpClient
  → 初始化新连接，创建新 CancellationTokenSource
```

**心跳超时状态机**：
```
正常接收数据 → 记录 LastDataTime
             ↓ (heartbeat_timeout_s 内无数据)
触发 OnHeartbeatTimeout
  → 关闭当前 TcpClient
  → 继续 AcceptClientConnections 等待新连接
  → BatteryDriverService 不停止，保留已注册的事件和 Channel
```

---

## 4. Strategy & Collectors

### 4.1 Data Flow

```
化成柜 → TCP → ReceiveBuffer 拆包验证 → 按帧头+长度分发
                                              ↓
               ┌── 0xFD (2696B) → ChannelData.cs    (336通道电压/电流)
               │── 0xFE (344B)  → EquipmentAlarm.cs (336通道异常报警)
               │── 0xFF (7B)    → CommandStatus.ProcessAck()  (命令ACK)
               │── 0xFF (65B)   → CommandStatus.ProcessState()(柜体7层状态)
               │── 0xFF (344B)  → CommandResult.cs  (化成结果 OK/NG)
               │── 0xEA (155B)  → WarningData.cs    (预警烟雾等)
               │
               ↓
          结构化数据 → Handler → DataPublisher
                            (Collector 直接调用 Handler.Publish, 绕过 Veneers)
                                              ↓
                              ┌── Channel<T>
                              ├── event Action<T>
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
        // raw 已由 ReceiveBuffer 验证为完整帧，可直接使用精确长度匹配
        switch (raw[0])
        {
            case 0xFD: _channelDataCollector.Process(raw); break;
            case 0xFE: _alarmCollector.Process(raw); break;
            case 0xFF: Dispatch0xFF(raw); break;   // 按长度三级分发
            case 0xEA: _warningDataCollector.Process(raw); break;
        }
    }

    private void Dispatch0xFF(byte[] raw)
    {
        // ReceiveBuffer 已验证 end 标记，此处只按长度路由
        switch (raw.Length)
        {
            case 7:   _commandStatusCollector.ProcessAck(raw); break;
            case 65:  _commandStatusCollector.ProcessState(raw); break;
            case 344: _commandResultCollector.Process(raw); break;
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

**关于 `OnStrategySweepCompleteInternalAsync`**：此方法为 battery-driver 需要在 base-driver 的 Handler 基类中新增的内部方法，在每次 Sweep 周期完成后由框架调用。需在 Handler.cs 中添加虚方法 `protected virtual Task OnStrategySweepCompleteInternalAsync()`，默认实现为空（`Task.CompletedTask`）。原项目 wc_turn_jf 中无对应实现。

### 4.3 Collectors

Each collector is responsible for parsing one frame type and producing structured data:

| Collector | Input (Start + Len) | Output Data | Original Code Reference |
|---|---|---|---|
| `ChannelData` | `0xFD` (2696 bytes) | `ChannelRealData` (336 × voltage + current) | `myTurnRealData` |
| `EquipmentAlarm` | `0xFE` (344 bytes) | `AlarmData` (336 channel flags, 0-7) | `myTurnError` |
| `CommandResult` | `0xFF` (344 bytes) | `ResultData` (OK=1/NG1=2/NG2=3 per channel) | `turn_result` |
| `WarningData` | `0xEA` (155 bytes) | `WarningData` (7层 × 电压/电流/参考值) | `myTcpErrorRecive` |
| `CommandStatus` | `0xFF` (7 bytes→ACK / 65 bytes→柜体状态) | `AckData` / `StatusData` | `Fun_turn_ack` / `Fun_turn_order` |

---

## 5. Data Models

All data structures are defined as `readonly record struct`（不可变，避免值拷贝后数组引用共享导致的数据竞争）。

```csharp
public readonly record struct ChannelRealData
{
    public byte CabinetIndex;        // 机柜编号 1-20
    public byte LeftRight;           // 左右侧 1-2
    public float[] Voltage;          // 336 elements (7层 × 48位置)
    public float[] Current;          // 336 elements (7层 × 48位置)
    public DateTime Timestamp;       // 接收时间戳
}

public readonly record struct AlarmData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] AbnormalFlags;     // 336 bytes, 0-7 (见 §3.2 异常值含义)
    public DateTime Timestamp;
}

public readonly record struct ResultData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] ChannelResults;    // 336 bytes: 1=OK, 2=NG1, 3=NG2
    public DateTime Timestamp;
}

public readonly record struct StatusData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public byte[] LayerStates;       // 7 bytes: 0=不处理, 1=启动, 2=暂停, 3=运行中, 4=完成
    public DateTime Timestamp;
}

// 命令 ACK（由 CommandStatus.ProcessAck 产出，完整匹配管道）
public readonly record struct AckData
{
    public ushort SeqNo;             // 对应下发命令的序号
    public byte Status;              // 1=成功, 0=失败
    public DateTime Timestamp;
}

public readonly record struct WarningData
{
    public byte CabinetIndex;
    public byte LeftRight;
    public WarningChannel[] Channels; // 7 elements (7层)
    public DateTime Timestamp;
}

public readonly record struct WarningChannel
{
    public byte Layer;               // 层号 1-7
    public float Voltage;            // 当前电压
    public float Current;            // 当前电流
    public float VoltageBefore;      // 参考电压（异常前）
    public float CurrentBefore;      // 参考电流（异常前）
    public int ChannelIndex;         // 通道索引（含层信息）
}
```

**索引公式**：`channelIndex = (layer - 1) * 48 + position`，其中 `layer` 1-7（帧中存储的层号）, `position` 0-47。

---

## 6. Transport: DataPublisher (Zero External Dependencies)

### 6.1 Design

Use `System.Threading.Channels` (built into .NET) as internal buffer, and C# events as the primary external interface. Data flow: `Collector → Handler → DataPublisher.Publish() → Channel + Event → 宿主`.

```csharp
public class DataPublisher : IDisposable
{
    // 按数据类型分发的独立 Channel（有界缓冲防 OOM）
    private readonly Channel<ChannelRealData> _channelData = Channel.CreateBounded<ChannelRealData>(1000);
    private readonly Channel<AlarmData> _alarmData = Channel.CreateBounded<AlarmData>(500);
    private readonly Channel<ResultData> _resultData = Channel.CreateBounded<ResultData>(200);
    private readonly Channel<StatusData> _statusData = Channel.CreateBounded<StatusData>(200);
    private readonly Channel<WarningData> _warningData = Channel.CreateBounded<WarningData>(100);
    private readonly Channel<AckData> _ackData = Channel.CreateBounded<AckData>(200);

    // 事件（宿主订阅）
    public event Action<ChannelRealData>? OnChannelData;
    public event Action<AlarmData>? OnAlarm;
    public event Action<ResultData>? OnResult;
    public event Action<StatusData>? OnStatus;
    public event Action<WarningData>? OnWarning;
    public event Action<AckData>? OnAck;
    public event Action<Exception, string>? OnError;  // (异常, 上下文描述)

    // === 发布方法（由 Handler 调用）===
    public void Publish(ChannelRealData data) { ... TryWrite + event ... }
    public void Publish(AlarmData data) { ... }
    public void Publish(ResultData data) { ... }
    public void Publish(StatusData data) { ... }
    public void Publish(WarningData data) { ... }
    public void Publish(AckData data) { ... }

    // === Channel Reader 暴露（供宿主批量消费）===
    public ChannelReader<ChannelRealData> GetChannelReader() => _channelData.Reader;
    public ChannelReader<AckData> GetAckReader() => _ackData.Reader;
    // ...类似其他类型

    // Channel 满时保护
    private bool TryWriteOrLog<T>(Channel<T> channel, T data, string channelName)
    {
        if (!channel.Writer.TryWrite(data))
        {
            OnError?.Invoke(
                new InvalidOperationException($"{channelName} channel full, data dropped"),
                "DataPublisher");
            return false;
        }
        return true;
    }
}
```

### 6.2 命令 ACK 关联机制

发出的命令通过 `BatteryDriverService.StartFormationAsync()` 进入以下流程：

```
StartFormationAsync(TurnOrder order)
  → 分配自增 seqNo
  → TcpConnection.SendAsync(byte[])           发送命令帧
  → PendingCommands.TryAdd(seqNo, tcs)         线程安全添加
  → 等待 TaskCompletionSource<AckData>.Task    异步等待 ACK
  → 超时 10s 未收到 ACK 则取消等待

Tcp 连接收到 ACK 帧 → CommandStatus.ProcessAck()
  → 解析出 seqNo 和 status
  → PendingCommands.TryRemove(seqNo, out tcs)  原子移除并获取
  → tcs?.TrySetResult(ackData)                 通知等待方

超时处理（启动时注册 Timer）:
  → 扫描 PendingCommands 中驻留超过 10s 的 entry
  → TryRemove(key, out tcs)
  → tcs?.TrySetException(new TimeoutException("命令 ACK 超时"))
  → OnError?.Invoke 记录超时日志
```

**线程安全**：使用 `ConcurrentDictionary<ushort, TaskCompletionSource<AckData>>`，所有操作（TryAdd、TryRemove、扫描）均为无锁并发安全。

**seqNo 溢出策略**：seqNo 类型为 `ushort`（0-65535）。自增到 `MaxValue` 后检查 `PendingCommands` 是否为空：
- 若无 pending 命令，安全回绕到 0。
- 若仍有 pending 命令，拒绝新命令并触发 `OnError` 事件（"seqNo exhausted: 65535 pending commands"），等待 pending 命令完成或超时后再重试。

**内存防护**：每个 entry 在 ACK 到达或超时后通过 `TryRemove` 立即清理，不会永久残留。使用 `CancellationTokenSource` 配合超时，避免 Timer 扫描对活跃 entry 产生竞争。

### 6.3 Why Channels + Events?

| Concern | Solution |
|---|---|
| 背压缓冲 | Channel 有界缓冲，避免快生产慢消费导致内存溢出 |
| 低延迟通知 | 事件同步触发(OnXxx)，适合实时场景 |
| 批量处理 | 支持宿主用 `ReadAllAsync().Buffer(N)` 批量消费 |
| 零外部依赖 | `System.Threading.Channels` 是 .NET 基础库的一部分 |
| Channel 满保护 | `TryWrite` 失败时触发 `OnError` 通知宿主，不阻塞生产方 |

### 6.4 宿主使用两种方式

```csharp
// 方式 A：事件（低延迟、简单）
driver.OnChannelData += data => {
    db.InsertChannelData(data);
};

// 方式 B：Channel 批处理（高频场景）
var reader = driver.GetChannelReader();
await foreach (var batch in reader.ReadAllAsync().Buffer(50))
{
    db.BulkInsert(batch);
}
```

### 6.5 BatteryHandler 职责

BatteryHandler 继承 base-driver 的 Handler 基类，在 Veneers 管道末端将结构化数据传递给 DataPublisher：

```csharp
public class BatteryHandler : Handler
{
    private readonly DataPublisher _publisher;

    // 各数据类型的发布委托（由 Veneers 管道末端调用）
    public void Publish(ChannelRealData data) => _publisher.Publish(data);
    public void Publish(AlarmData data) => _publisher.Publish(data);
    public void Publish(ResultData data) => _publisher.Publish(data);
    public void Publish(StatusData data) => _publisher.Publish(data);
    public void Publish(WarningData data) => _publisher.Publish(data);
    public void Publish(AckData data) => _publisher.Publish(data);
}
```

**BatteryDriverService 与 DataPublisher 的关系**：
- `DataPublisher` 是内部传输组件，负责 Channel 缓冲 + 事件触发。
- `BatteryHandler` 将 Veneers 输出的数据转发给 DataPublisher。
- `BatteryDriverService` 对外暴露事件，内部委托给 `DataPublisher` 的事件。
- 宿主统一订阅 `BatteryDriverService` 的事件，不直接与 DataPublisher 交互。

---

## 7. BatteryDriverService — External Entry Point

```csharp
public class BatteryDriverService : BackgroundService
{
    // 事件（委托给内部 DataPublisher）
    public event Action<ChannelRealData>? OnChannelData;
    public event Action<AlarmData>? OnAlarm;
    public event Action<ResultData>? OnResult;
    public event Action<StatusData>? OnStatus;
    public event Action<WarningData>? OnWarning;
    public event Action<AckData>? OnAck;
    public event Action<Exception, string>? OnError;

    // Commands（返回 Task<AckData> 异步等待 ACK）
    public Task<AckData> StartFormationAsync(TurnOrder order);
    public Task<AckData> PauseFormationAsync(byte cabinet, byte leftRight);
    public Task<AckData> ResumeFormationAsync(byte cabinet, byte leftRight);
    public DriverStatus GetStatus();

    // Channel Reader 访问（宿主高频批量消费）
    public ChannelReader<ChannelRealData> GetChannelReader();
    public ChannelReader<AckData> GetAckReader();
    // ... 类似其他类型
}
```

**TurnOrder 定义**：
```csharp
public readonly record struct TurnOrder
{
    public byte CabinetIndex;       // 目标机柜
    public byte LeftRight;          // 左右侧
    public string? Technology;      // 工艺参数（对应协议中 50 字节 technology 字段）
    public byte[] LayerCommands;    // 7 elements: 0=不处理, 1=启动, 2=暂停, 6=恢复
}
```

**DriverStatus 定义**：
```csharp
public readonly record struct DriverStatus
{
    public bool IsConnected;                    // TCP 是否已连接（单连接模式，true=已连接）
    public DateTime? LastDataReceivedAt;        // 最后数据接收时间
    public int ChannelDataQueueLength;          // Channel 积压数
    public int AlarmDataQueueLength;
    public int ResultDataQueueLength;
    public int PendingCommandCount;             // 等待 ACK 的命令数
}
```

**启动序列**（`StartAsync`）：
```
BatteryDriverService.StartAsync(CancellationToken)
  → Bootstrap.Start(args)                   加载 YAML 配置
  → Machines.CreateMachines(config)         创建 Machine
  → TcpConnection = Machine[connection]     TCP Server 启动监听
  → Strategy.InitializeAsync()              注册数据回调
  → Sweep loop (heartbeat only)             进入后台扫描
  → 0xEA 预警连接（如配置独立端口）并行启动
```

**关闭序列**（`StopAsync`）：
```
BatteryDriverService.StopAsync(CancellationToken)
  → CancellationToken 传播：通知 Sweep loop、TCP 接收循环、PendingCommands 超时扫描停止
  → 停止所有 TcpConnection 实例（数据端口 + 预警端口独立连接）
     每个调用 StopAsync()                     停止对应 Listener，关闭客户端
  → PendingCommands 清理：所有未完成的 TaskCompletionSource
    调用 TrySetCanceled(cancellationToken)   通知等待方法"驱动已关闭"
  → Channel 和事件保留（宿主可能仍在消费存量数据）
  → Strategy.Dispose() + DataPublisher.Dispose()
```

---

## 8. YAML Configuration

Three-file structure matching fanuc-driver conventions:

### config.system.yml
```yaml
machine-base: &machine-base
  enabled: true
  type: CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver
  strategy: CollectionDrivers.BatteryDriver.Strategies.BatteryTcpStrategy, CollectionDrivers.BatteryDriver
  handler: CollectionDrivers.BatteryDriver.BatteryHandler, CollectionDrivers.BatteryDriver
```

### config.user.yml
```yaml
source-1: &source-1
  CollectionDrivers.BatteryDriver.BatteryMachine, CollectionDrivers.BatteryDriver:
    sweep_ms: 1000
    net:
      port: 13000
      heartbeat_timeout_s: 60
      # 可选：预警数据独立端口（0 表示禁用独立连接，预警帧从主端口接收）
      warning_port: 13100
      warning_heartbeat_timeout_s: 60
```

### config.machines.yml
```yaml
machines:
  - id: cabinet_1
    <<: *machine-base
    <<: *source-1
    CollectionDrivers.BatteryDriver.Strategies.BatteryTcpStrategy, CollectionDrivers.BatteryDriver:
      collectors:
      - CollectionDrivers.BatteryDriver.Collectors.ChannelData, battery-driver
      - CollectionDrivers.BatteryDriver.Collectors.EquipmentAlarm, battery-driver
      - CollectionDrivers.BatteryDriver.Collectors.CommandResult, battery-driver
```

> **关于 collectors 列表**：此列表为配置文档，用于声明该 Strategy 启用的 Collector 集合。实际 Collector 实例在 `BatteryTcpStrategy` 构造函数中硬编码为私有字段（`_channelDataCollector` 等），框架不通过 YAML 动态实例化 Collector。如需通过框架自动注入，需在后续版本中扩展 base-driver 的依赖注入机制。

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
