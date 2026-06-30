using System.Collections.Concurrent;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver;

/// <summary>
/// 待确认命令管理器。为每条下发命令分配 seqNo，通过 TaskCompletionSource 跟踪 ACK 响应。
/// 内置超时扫描（默认 10 秒），超时自动取消并触发错误回调。
/// </summary>
public class PendingCommandManager : IDisposable
{
    private readonly ConcurrentDictionary<ushort, PendingEntry> _pending = new();
    private ushort _nextSeqNo;
    private readonly Timer _timeoutTimer;
    private readonly Action<Exception, string>? _onError;
    private const int DefaultTimeoutSeconds = 10;

    /// <summary>构造命令管理器</summary>
    /// <param name="onError">超时或异常时的错误回调</param>
    public PendingCommandManager(Action<Exception, string>? onError = null)
    {
        _onError = onError;
        _timeoutTimer = new Timer(ScanTimeout, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>分配下一个序列号（线程安全）</summary>
    public ushort NextSeqNo()
    {
        // Thread-safe increment via lock for the check-and-increment pattern
        lock (this)
        {
            if (_nextSeqNo == ushort.MaxValue && _pending.Count > 0)
                throw new InvalidOperationException("seqNo exhausted: all 65535 slots in use");
            return _nextSeqNo++;
        }
    }

    /// <summary>原子分配 seqNo 并注册待确认命令。返回 seqNo 和可等待的 Task。</summary>
    public (ushort seqNo, Task<AckData> task) RegisterCommand()
    {
        var seqNo = NextSeqNo();
        var tcs = new TaskCompletionSource<AckData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry(tcs, DateTime.UtcNow);

        if (!_pending.TryAdd(seqNo, entry))
            throw new InvalidOperationException($"seqNo {seqNo} already pending");

        return (seqNo, tcs.Task);
    }

    /// <summary>尝试完成指定 seqNo 的命令（收到 ACK 时调用）</summary>
    public bool TryComplete(ushort seqNo, AckData ack)
    {
        if (_pending.TryRemove(seqNo, out var entry))
            return entry.Tcs.TrySetResult(ack);
        return false;
    }

    private void ScanTimeout(object? _)
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(DefaultTimeoutSeconds);
        foreach (var kvp in _pending)
        {
            var entry = kvp.Value;
            if (entry.Tcs.Task.IsCompleted || entry.Tcs.Task.IsCanceled || entry.Tcs.Task.IsFaulted)
            {
                if (_pending.TryRemove(kvp.Key, out PendingEntry _))
                    continue;
            }

            // Timeout-based eviction
            if (entry.CreatedAt < cutoff)
            {
                if (_pending.TryRemove(kvp.Key, out PendingEntry removed))
                {
                    _onError?.Invoke(
                        new TimeoutException($"Command seqNo={kvp.Key} timed out after {DefaultTimeoutSeconds}s"),
                        "PendingCommandManager.ScanTimeout");
                    removed.Tcs.TrySetException(
                        new TimeoutException($"Command ACK timeout (seqNo={kvp.Key})"));
                }
            }
        }
    }

    /// <summary>释放资源，取消所有待确认命令</summary>
    public void Dispose()
    {
        _timeoutTimer.Dispose();
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var entry))
                entry.Tcs.TrySetCanceled();
        }
    }

    private readonly record struct PendingEntry(TaskCompletionSource<AckData> Tcs, DateTime CreatedAt);
}
