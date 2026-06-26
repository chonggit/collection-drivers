using battery.driver.collectors;
using battery.driver.connections;
using l99.driver.@base;

namespace battery.driver.strategies;

public class BatteryTcpStrategy : Strategy
{
    private TcpConnection? _connection;
    private TcpConnection? _warningConnection;
    private readonly ChannelData _channelDataCollector = new();
    private readonly EquipmentAlarm _alarmCollector = new();
    private readonly CommandResult _commandResultCollector = new();
    private readonly CommandStatus _commandStatusCollector = new();
    private readonly WarningData _warningDataCollector = new();
    private PendingCommandManager? _pendingCommands;
    private bool _disposed;

    public ChannelData ChannelDataCollector => _channelDataCollector;
    public EquipmentAlarm AlarmCollector => _alarmCollector;
    public CommandResult CommandResultCollector => _commandResultCollector;
    public CommandStatus CommandStatusCollector => _commandStatusCollector;
    public WarningData WarningDataCollector => _warningDataCollector;

    public event Action<Exception, string>? OnError;

    public BatteryTcpStrategy(Machine machine) : base(machine)
    {
    }

    public override async Task<dynamic?> InitializeAsync()
    {
        var rawConfig = Machine.Configuration.strategy;
        int port = rawConfig.ContainsKey("port") ? (int)rawConfig["port"] : 13000;
        int warningPort = rawConfig.ContainsKey("warning_port") ? (int)rawConfig["warning_port"] : 13100;
        int heartbeatTimeout = rawConfig.ContainsKey("heartbeat_timeout_s")
            ? (int)rawConfig["heartbeat_timeout_s"] : 60;

        _pendingCommands = new PendingCommandManager(
            (ex, ctx) => OnError?.Invoke(ex, ctx));

        _connection = new TcpConnection(port, heartbeatTimeout);
        _connection.OnDataReceived += OnRawDataReceived;
        await _connection.StartListeningAsync();

        try
        {
            _warningConnection = new TcpConnection(warningPort, heartbeatTimeout);
            _warningConnection.OnDataReceived += OnRawDataReceived;
            await _warningConnection.StartListeningAsync();
        }
        catch (Exception ex)
        {
            _warningConnection?.Dispose();
            _warningConnection = null;
            OnError?.Invoke(ex, $"Warning port {warningPort} unavailable");
        }

        return null;
    }

    private void OnRawDataReceived(byte[] raw)
    {
        // ACK 帧：先通知 PendingCommandManager，再分发给 Collector
        if (raw.Length == 7 && raw[0] == 0xFF && raw[6] == 0xEF)
        {
            var seqNo = (ushort)((raw[3] << 8) | raw[4]);
            var ack = new battery.driver.models.AckData { SeqNo = seqNo, Status = raw[5], Timestamp = DateTime.UtcNow };
            _pendingCommands?.TryComplete(seqNo, ack);
        }

        switch (raw[0])
        {
            case 0xFD: _channelDataCollector.Process(raw); break;
            case 0xFE: _alarmCollector.Process(raw); break;
            case 0xFF: Dispatch0xFF(raw); break;
            case 0xEA: _warningDataCollector.Process(raw); break;
        }
    }

    private void Dispatch0xFF(byte[] raw)
    {
        switch (raw.Length)
        {
            case 7:   _commandStatusCollector.ProcessAck(raw); break;
            case 65:  _commandStatusCollector.ProcessState(raw); break;
            case 344: _commandResultCollector.Process(raw); break;
        }
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);
        _connection?.CheckHeartbeat();
        LastSuccess = _connection?.IsConnected ?? false;
        IsHealthy = _connection?.IsConnected ?? false;
        if (Machine?.Handler != null)
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }

    // ================ Commands ================

    public async Task<battery.driver.models.AckData> StartFormationAsync(battery.driver.models.TurnOrder order)
    {
        return await SendCommandAsync(order.CabinetIndex, order.LeftRight, order.LayerCommands, 0x01, order.Technology);
    }

    public Task<battery.driver.models.AckData> PauseFormationAsync(byte cabinet, byte leftRight)
    {
        byte[] layerCmds = [0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02];
        return SendCommandAsync(cabinet, leftRight, layerCmds, 0x02, null);
    }

    public Task<battery.driver.models.AckData> ResumeFormationAsync(byte cabinet, byte leftRight)
    {
        byte[] layerCmds = [0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06];
        return SendCommandAsync(cabinet, leftRight, layerCmds, 0x06, null);
    }

    private async Task<battery.driver.models.AckData> SendCommandAsync(byte cabinet, byte leftRight, byte[] layerCommands, byte commandType, string? technology)
    {
        if (_pendingCommands == null)
            throw new InvalidOperationException("Strategy not initialized");

        var (seqNo, task) = _pendingCommands.RegisterCommand();

        var frame = new byte[65];
        frame[0] = 0xFF;
        frame[1] = 0x00; frame[2] = 0x3F;
        frame[3] = (byte)(seqNo >> 8); frame[4] = (byte)seqNo;
        frame[5] = cabinet;
        frame[6] = leftRight;
        if (technology != null)
        {
            var techBytes = System.Text.Encoding.UTF8.GetBytes(technology);
            Array.Copy(techBytes, 0, frame, 7, Math.Min(techBytes.Length, 50));
        }
        if (layerCommands != null && layerCommands.Length > 0)
            Array.Copy(layerCommands, 0, frame, 57, Math.Min(layerCommands.Length, 7));
        frame[57] = commandType;
        frame[64] = 0xEF;

        await _connection!.SendAsync(frame);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completed = await Task.WhenAny(task, Task.Delay(-1, cts.Token));
        if (completed == task)
            return await task;

        throw new TimeoutException($"Command ACK timeout (seqNo={seqNo})");
    }

    // ================ Lifecycle ================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingCommands?.Dispose();
        _warningConnection?.Dispose();
        _warningConnection = null;
        _connection?.Dispose();
        _connection = null;
    }
}
