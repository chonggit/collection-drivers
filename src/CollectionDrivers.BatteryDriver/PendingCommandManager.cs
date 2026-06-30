using System.Collections.Concurrent;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver;

public class PendingCommandManager : IDisposable
{
    // Track creation time alongside TCS for timeout scanning
    private readonly ConcurrentDictionary<ushort, PendingEntry> _pending = new();
    private ushort _nextSeqNo;
    private readonly Timer _timeoutTimer;
    private readonly Action<Exception, string>? _onError;
    private const int DefaultTimeoutSeconds = 10;

    public PendingCommandManager(Action<Exception, string>? onError = null)
    {
        _onError = onError;
        _timeoutTimer = new Timer(ScanTimeout, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

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

    /// <summary>
    /// Atomically allocate a seqNo and register the pending command.
    /// Returns both the allocated seqNo and the Task the caller can await.
    /// </summary>
    public (ushort seqNo, Task<AckData> task) RegisterCommand()
    {
        var seqNo = NextSeqNo();
        var tcs = new TaskCompletionSource<AckData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry(tcs, DateTime.UtcNow);

        if (!_pending.TryAdd(seqNo, entry))
            throw new InvalidOperationException($"seqNo {seqNo} already pending");

        return (seqNo, tcs.Task);
    }

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
