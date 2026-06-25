using System.Threading.Channels;

namespace drivers.common;

/// <summary>
/// 泛型数据发布器：Channel<T> + 事件 Action<T> 双通道。
/// 生产方调用 Publish(data)，消费方可选事件订阅或 Channel 批量消费。
/// </summary>
public class DataPublisher<T> : IDisposable
{
    private readonly Channel<T> _channel;

    public event Action<T>? OnData;
    public event Action<Exception, string>? OnError;

    private readonly int _capacity;

    public DataPublisher(int boundedCapacity = 500)
    {
        _capacity = boundedCapacity;
        _channel = Channel.CreateBounded<T>(_capacity);
    }

    /// <summary>发布一条数据（写 Channel + 触发事件）</summary>
    public void Publish(T data)
    {
        if (!_channel.Writer.TryWrite(data))
            OnError?.Invoke(
                new InvalidOperationException(
                    $"{typeof(T).Name} channel full (capacity={_capacity})"),
                "DataPublisher");

        OnData?.Invoke(data);
    }

    /// <summary>Channel 读取端（供宿主批量消费）</summary>
    public ChannelReader<T> Reader => _channel.Reader;

    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}
