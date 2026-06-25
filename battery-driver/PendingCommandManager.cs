using System.Collections.Concurrent;
using battery.driver.models;

namespace battery.driver;

public class PendingCommandManager : IDisposable
{
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<AckData>> _pending = new();
    private ushort _nextSeqNo;
    private readonly Timer _timeoutTimer;
    private readonly Action<Exception, string>? _onError;

    public PendingCommandManager(Action<Exception, string>? onError = null)
    {
        _onError = onError;
        _timeoutTimer = new Timer(ScanTimeout, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ushort NextSeqNo()
    {
        if (_nextSeqNo == ushort.MaxValue && _pending.Count > 0)
            throw new InvalidOperationException("seqNo exhausted: all 65535 slots in use");
        return _nextSeqNo++;
    }

    public Task<AckData> RegisterCommand()
    {
        var seqNo = NextSeqNo();
        var tcs = new TaskCompletionSource<AckData>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(seqNo, tcs))
            throw new InvalidOperationException($"seqNo {seqNo} already pending");

        return tcs.Task;
    }

    public bool TryComplete(ushort seqNo, AckData ack)
    {
        if (_pending.TryRemove(seqNo, out var tcs))
            return tcs.TrySetResult(ack);
        return false;
    }

    public void CancelAll(CancellationToken cancellationToken)
    {
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled(cancellationToken);
        }
    }

    public int PendingCount => _pending.Count;

    private void ScanTimeout(object? _)
    {
        foreach (var kvp in _pending)
        {
            if (kvp.Value.Task.IsCompleted || kvp.Value.Task.IsCanceled || kvp.Value.Task.IsFaulted)
            {
                _pending.TryRemove(kvp.Key, out var _);
            }
        }
    }

    public void Dispose()
    {
        _timeoutTimer.Dispose();
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetCanceled();
        }
    }
}
