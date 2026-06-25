using battery.driver.channels;
using battery.driver.connections;
using battery.driver.models;
using Microsoft.Extensions.Hosting;

namespace battery.driver;

public class BatteryDriverService : BackgroundService
{
    private readonly DataPublisher _publisher = new();
    private readonly PendingCommandManager _pendingCommands;
    private TcpConnection? _dataConnection;
    private TcpConnection? _warningConnection;
    private DateTime _lastDataReceived;

    public event Action<ChannelRealData>? OnChannelData { add => _publisher.OnChannelData += value; remove => _publisher.OnChannelData -= value; }
    public event Action<AlarmData>? OnAlarm { add => _publisher.OnAlarm += value; remove => _publisher.OnAlarm -= value; }
    public event Action<ResultData>? OnResult { add => _publisher.OnResult += value; remove => _publisher.OnResult -= value; }
    public event Action<StatusData>? OnStatus { add => _publisher.OnStatus += value; remove => _publisher.OnStatus -= value; }
    public event Action<WarningData>? OnWarning { add => _publisher.OnWarning += value; remove => _publisher.OnWarning -= value; }
    public event Action<AckData>? OnAck { add => _publisher.OnAck += value; remove => _publisher.OnAck -= value; }
    public event Action<Exception, string>? OnError;

    public BatteryDriverService()
    {
        _pendingCommands = new PendingCommandManager(OnError);
    }

    internal BatteryDriverService(DataPublisher publisher) : this()
    {
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configPort = 13000;
        var warningPort = 13100;
        var heartbeatTimeout = 60;

        _dataConnection = new TcpConnection(configPort, heartbeatTimeout);
        _dataConnection.OnDataReceived += OnRawData;
        _dataConnection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);

        if (warningPort > 0)
        {
            _warningConnection = new TcpConnection(warningPort, heartbeatTimeout);
            _warningConnection.OnDataReceived += OnRawData;
            _warningConnection.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);
        }

        await _dataConnection.StartListeningAsync(stoppingToken);
        if (_warningConnection != null)
            _ = _warningConnection.StartListeningAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
            _dataConnection.CheckHeartbeat();
            _warningConnection?.CheckHeartbeat();
        }
    }

    private void OnRawData(byte[] frame)
    {
        _lastDataReceived = DateTime.UtcNow;
        switch (frame[0])
        {
            case 0xFD: ParseChannelData(frame); break;
            case 0xFE: ParseAlarm(frame); break;
            case 0xFF: Dispatch0xFF(frame); break;
            case 0xEA: ParseWarning(frame); break;
        }
    }

    private void ParseChannelData(byte[] frame)
    {
        if (frame.Length < 2696 || frame[0] != 0xFD)
        {
            OnError?.Invoke(new InvalidDataException($"Invalid ChannelData frame: len={frame.Length}, start=0x{frame[0]:X2}"), "ParseChannelData");
            return;
        }
        var voltage = new float[336];
        var current = new float[336];
        for (int i = 0; i < 336; i++)
        {
            voltage[i] = BitConverter.ToSingle(frame, 7 + i * 4);
            current[i] = BitConverter.ToSingle(frame, 7 + 1344 + i * 4);
        }
        _publisher.Publish(new ChannelRealData
        {
            CabinetIndex = frame[5], LeftRight = frame[6],
            Voltage = voltage, Current = current,
            Timestamp = DateTime.UtcNow
        });
    }

    private void ParseAlarm(byte[] frame)
    {
        if (frame.Length < 344 || frame[0] != 0xFE)
        {
            OnError?.Invoke(new InvalidDataException($"Invalid Alarm frame: len={frame.Length}, start=0x{frame[0]:X2}"), "ParseAlarm");
            return;
        }
        var flags = new byte[336];
        Array.Copy(frame, 7, flags, 0, 336);
        _publisher.Publish(new AlarmData
        {
            CabinetIndex = frame[5], LeftRight = frame[6],
            AbnormalFlags = flags, Timestamp = DateTime.UtcNow
        });
    }

    private void Dispatch0xFF(byte[] frame)
    {
        switch (frame.Length)
        {
            case 7:
                if (frame[0] != 0xFF || frame[6] != 0xEF) return;
                var seqNo = (ushort)((frame[3] << 8) | frame[4]);
                var ack = new AckData { SeqNo = seqNo, Status = frame[5], Timestamp = DateTime.UtcNow };
                _pendingCommands.TryComplete(seqNo, ack);
                _publisher.Publish(ack);
                break;
            case 65:
                if (frame[0] != 0xFF || frame[64] != 0xEF) return;
                var states = new byte[7];
                Array.Copy(frame, 57, states, 0, 7);
                _publisher.Publish(new StatusData
                {
                    CabinetIndex = frame[5], LeftRight = frame[6],
                    LayerStates = states, Timestamp = DateTime.UtcNow
                });
                break;
            case 344:
                if (frame[0] != 0xFF || frame[343] != 0xEF) return;
                var results = new byte[336];
                Array.Copy(frame, 7, results, 0, 336);
                _publisher.Publish(new ResultData
                {
                    CabinetIndex = frame[5], LeftRight = frame[6],
                    ChannelResults = results, Timestamp = DateTime.UtcNow
                });
                break;
            default:
                OnError?.Invoke(new InvalidDataException($"Unknown 0xFF frame length: {frame.Length}"), "Dispatch0xFF");
                break;
        }
    }

    private void ParseWarning(byte[] frame)
    {
        if (frame.Length < 155 || frame[0] != 0xEA || frame[154] != 0xED)
        {
            OnError?.Invoke(new InvalidDataException($"Invalid Warning frame: len={frame.Length}, start=0x{frame[0]:X2}"), "ParseWarning");
            return;
        }
        var channels = new WarningChannel[7];
        for (int i = 0; i < 7; i++)
        {
            int off = 7 + i * 21;
            channels[i] = new WarningChannel
            {
                Layer = frame[off],
                Voltage = BitConverter.ToSingle(frame, off + 1),
                Current = BitConverter.ToSingle(frame, off + 5),
                VoltageBefore = BitConverter.ToSingle(frame, off + 9),
                CurrentBefore = BitConverter.ToSingle(frame, off + 13),
                ChannelIndex = BitConverter.ToInt32(frame, off + 17)
            };
        }
        _publisher.Publish(new WarningData
        {
            CabinetIndex = frame[5], LeftRight = frame[6],
            Channels = channels, Timestamp = DateTime.UtcNow
        });
    }

    public async Task<AckData> StartFormationAsync(TurnOrder order)
    {
        return await SendCommandAsync(order.CabinetIndex, order.LeftRight, order.LayerCommands, 0x01, order.Technology);
    }

    public Task<AckData> PauseFormationAsync(byte cabinet, byte leftRight)
    {
        return SendCommandAsync(cabinet, leftRight, new byte[7], 0x02, null);
    }

    public Task<AckData> ResumeFormationAsync(byte cabinet, byte leftRight)
    {
        return SendCommandAsync(cabinet, leftRight, new byte[7], 0x06, null);
    }

    private async Task<AckData> SendCommandAsync(byte cabinet, byte leftRight, byte[] layerCommands, byte commandType, string? technology)
    {
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
        // Write layer commands first, then ensure byte 57 has the command type
        if (layerCommands != null && layerCommands.Length > 0)
            Array.Copy(layerCommands, 0, frame, 57, Math.Min(layerCommands.Length, 7));
        frame[57] = commandType; // command type always goes to byte 57
        frame[64] = 0xEF;

        await _dataConnection!.SendAsync(frame);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var completed = await Task.WhenAny(task, Task.Delay(-1, cts.Token));
        if (completed == task)
            return await task;

        throw new TimeoutException($"Command ACK timeout (seqNo={seqNo})");
    }

    public DriverStatus GetStatus() => new()
    {
        IsConnected = _dataConnection?.IsConnected ?? false,
        LastDataReceivedAt = _lastDataReceived == default ? null : _lastDataReceived,
        PendingCommandCount = _pendingCommands.PendingCount
    };

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pendingCommands.CancelAll(cancellationToken);
        if (_dataConnection != null)
            await _dataConnection.StopAsync();
        if (_warningConnection != null)
            await _warningConnection.StopAsync();
        _publisher.Dispose();
        _pendingCommands.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
