using System.Threading.Channels;
using battery.driver.models;

namespace battery.driver.channels;

public class DataPublisher : IDisposable
{
    private readonly Channel<ChannelRealData> _channelData = Channel.CreateBounded<ChannelRealData>(1000);
    private readonly Channel<AlarmData> _alarmData = Channel.CreateBounded<AlarmData>(500);
    private readonly Channel<ResultData> _resultData = Channel.CreateBounded<ResultData>(200);
    private readonly Channel<StatusData> _statusData = Channel.CreateBounded<StatusData>(200);
    private readonly Channel<WarningData> _warningData = Channel.CreateBounded<WarningData>(100);
    private readonly Channel<AckData> _ackData = Channel.CreateBounded<AckData>(200);

    public event Action<ChannelRealData>? OnChannelData;
    public event Action<AlarmData>? OnAlarm;
    public event Action<ResultData>? OnResult;
    public event Action<StatusData>? OnStatus;
    public event Action<WarningData>? OnWarning;
    public event Action<AckData>? OnAck;
    public event Action<Exception, string>? OnError;

    public void Publish(ChannelRealData data) => PublishWithEvent(_channelData, data, ref OnChannelData);
    public void Publish(AlarmData data) => PublishWithEvent(_alarmData, data, ref OnAlarm);
    public void Publish(ResultData data) => PublishWithEvent(_resultData, data, ref OnResult);
    public void Publish(StatusData data) => PublishWithEvent(_statusData, data, ref OnStatus);
    public void Publish(WarningData data) => PublishWithEvent(_warningData, data, ref OnWarning);
    public void Publish(AckData data) => PublishWithEvent(_ackData, data, ref OnAck);

    private void PublishWithEvent<T>(Channel<T> channel, T data, ref Action<T>? eventHandler)
    {
        if (!channel.Writer.TryWrite(data))
            OnError?.Invoke(new InvalidOperationException($"{typeof(T).Name} channel full"), "DataPublisher");
        eventHandler?.Invoke(data);
    }

    public ChannelReader<ChannelRealData> GetChannelDataReader() => _channelData.Reader;
    public ChannelReader<AlarmData> GetAlarmReader() => _alarmData.Reader;
    public ChannelReader<ResultData> GetResultReader() => _resultData.Reader;
    public ChannelReader<StatusData> GetStatusReader() => _statusData.Reader;
    public ChannelReader<WarningData> GetWarningReader() => _warningData.Reader;
    public ChannelReader<AckData> GetAckReader() => _ackData.Reader;

    public void Dispose()
    {
        _channelData.Writer.TryComplete();
        _alarmData.Writer.TryComplete();
        _resultData.Writer.TryComplete();
        _statusData.Writer.TryComplete();
        _warningData.Writer.TryComplete();
        _ackData.Writer.TryComplete();
    }
}
