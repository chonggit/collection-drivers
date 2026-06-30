using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Connections;
using CollectionDrivers.Common;

namespace CollectionDrivers.BatteryDriver.Strategies;

public class BatteryTcpStrategy : Strategy, IDisposable
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

    public override async Task InitializeAsync()
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

        return;
    }

    private void OnRawDataReceived(byte[] raw)
    {
        // ACK 帧：先通知 PendingCommandManager，再分发给 Collector
        if (raw.Length == 7 && raw[0] == 0xFF && raw[6] == 0xEF)
        {
            var seqNo = (ushort)((raw[3] << 8) | raw[4]);
            var ack = new Models.AckData { SeqNo = seqNo, Status = raw[5], Timestamp = DateTime.UtcNow };
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
