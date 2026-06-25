using System.Net;
using CableRobot.Fins;

namespace fins.driver;

public class FinsConnection : IDisposable
{
    private FinsClient? _client;
    private readonly string _remoteIp;
    private readonly int _port;
    private readonly int _timeoutMs;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public bool IsConnected => _client != null && !_disposed;

    public event Action<Exception, string>? OnError;

    public FinsConnection(string remoteIp, int port = 9600, int timeoutMs = 2000)
    {
        _remoteIp = remoteIp;
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public void Connect()
    {
        if (_client != null)
        {
            _client.Close();
            _client.Dispose();
        }

        var endpoint = new IPEndPoint(IPAddress.Parse(_remoteIp), _port);
        _client = new FinsClient(endpoint);
        _client.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);
    }

    // ct 仅控制信号量等待。读操作超时由 FinsClient.Timeout 控制。
    public async Task<ushort[]> ReadDAsync(ushort startAddress, ushort count, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Not connected");

        await _lock.WaitAsync(ct);
        try
        {
            return await _client.ReadDataAsync(startAddress, count);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ct 仅控制信号量等待。写操作超时由 FinsClient.Timeout 控制。
    public async Task WriteDAsync(ushort startAddress, ushort[] data, CancellationToken ct = default)
    {
        if (_client == null)
            throw new InvalidOperationException("Not connected");

        await _lock.WaitAsync(ct);
        try
        {
            await _client.WriteDataAsync(startAddress, data);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _client?.Close();
        _client?.Dispose();
        _client = null;
        _lock.Dispose();
    }
}
