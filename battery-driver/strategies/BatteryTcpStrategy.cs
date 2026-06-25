using battery.driver.collectors;
using battery.driver.connections;
using l99.driver.@base;

namespace battery.driver.strategies;

public class BatteryTcpStrategy : Strategy
{
    private TcpConnection? _connection;
    private readonly ChannelData _channelDataCollector = new();
    private readonly EquipmentAlarm _alarmCollector = new();
    private readonly CommandResult _commandResultCollector = new();
    private readonly CommandStatus _commandStatusCollector = new();
    private readonly WarningData _warningDataCollector = new();

    public ChannelData ChannelDataCollector => _channelDataCollector;
    public EquipmentAlarm AlarmCollector => _alarmCollector;
    public CommandResult CommandResultCollector => _commandResultCollector;
    public CommandStatus CommandStatusCollector => _commandStatusCollector;
    public WarningData WarningDataCollector => _warningDataCollector;

    public BatteryTcpStrategy(Machine machine) : base(machine)
    {
    }

    public void SetConnection(TcpConnection connection)
    {
        _connection = connection;
        connection.OnDataReceived += OnRawDataReceived;
    }

    private void OnRawDataReceived(byte[] raw)
    {
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

    public override async Task<dynamic?> InitializeAsync()
    {
        if (_connection != null)
            await _connection.StartListeningAsync();
        return null;
    }

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);
        _connection?.CheckHeartbeat();
        if (Machine?.Handler != null)
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }
}
