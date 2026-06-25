using System.Net.Sockets;

namespace drivers.common;

public class TcpClientConnection : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _disposeCts;
    private Task? _receiveTask;
    private readonly List<byte> _recvBuffer = new();
    private const int MaxReceiveBuffer = 65536;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public string Host { get; private set; } = "";
    public int Port { get; private set; }
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public bool IsConnected => _client?.Connected ?? false;

    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<Exception, string>? OnError;
    public event Action<byte[]>? OnDataReceived;

    public byte[]? FrameDelimiter { get; set; }

    public void Configure(string host, int port,
        int connectTimeoutMs = 3000, int receiveTimeoutMs = 5000)
    {
        Host = host;
        Port = port;
        ConnectTimeoutMs = connectTimeoutMs;
        ReceiveTimeoutMs = receiveTimeoutMs;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

        _client?.Dispose();
        _client = new TcpClient();
        await _client.ConnectAsync(Host, Port, linkedCts.Token);
        _stream = _client.GetStream();
        _client.ReceiveTimeout = ReceiveTimeoutMs;
        _recvBuffer.Clear();
        OnConnected?.Invoke();
    }

    // ct 仅控制信号量等待和重试取消。读写超时由 ReceiveTimeoutMs + 外部 CTS 控制。
    public async Task<byte[]> SendAndReceiveAsync(byte[] command,
        int retryCount = 3, CancellationToken ct = default)
    {
        int attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!IsConnected) await ReconnectAsync(ct);

                await _stream!.WriteAsync(command, ct);

                while (!ct.IsCancellationRequested)
                {
                    var available = _client!.Available;
                    if (available > 0)
                    {
                        var buf = new byte[available];
                        var len = await _stream.ReadAsync(buf, 0, buf.Length, ct);
                        _recvBuffer.AddRange(buf.Take(len));

                        if (_recvBuffer.Count > MaxReceiveBuffer)
                        {
                            _recvBuffer.Clear();
                            throw new InvalidOperationException("ReceiveBuffer overflow");
                        }

                        var frame = TryExtractFrame();
                        if (frame != null) return frame;
                    }
                    else
                    {
                        await Task.Delay(10, ct);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                attempt++;
                OnError?.Invoke(ex, $"SendAndReceive (attempt {attempt})");
                if (attempt >= retryCount) throw;
                await Task.Delay(100 * attempt, ct);
                await ReconnectAsync(ct);
            }
        }
    }

    public void StartReceiveLoop()
    {
        _disposeCts = new CancellationTokenSource();
        var token = _disposeCts.Token;
        _receiveTask = Task.Run(async () =>
        {
            int retryDelay = 1000;
            const int maxRetryDelay = 30000;

            while (!token.IsCancellationRequested && !_disposed)
            {
                try
                {
                    await ConnectAsync(token);
                    retryDelay = 1000;
                    await ReceiveLoop(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    OnError?.Invoke(ex, "ReceiveLoop.Reconnect");
                }
                await Task.Delay(retryDelay, token);
                retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
            }
        });
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && IsConnected && !_disposed)
        {
            try
            {
                var len = await _stream!.ReadAsync(buffer, 0, buffer.Length, ct);
                if (len == 0) break;

                _recvBuffer.AddRange(buffer.Take(len));

                if (_recvBuffer.Count > MaxReceiveBuffer)
                {
                    OnError?.Invoke(
                        new InvalidOperationException("ReceiveBuffer overflow"), "ReceiveLoop");
                    _recvBuffer.Clear();
                    continue;
                }

                while (true)
                {
                    var frame = TryExtractFrame();
                    if (frame == null) break;
                    OnDataReceived?.Invoke(frame);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, "ReceiveLoop");
                break;
            }
        }
        OnDisconnected?.Invoke();
    }

    private byte[]? TryExtractFrame()
    {
        if (FrameDelimiter == null || FrameDelimiter.Length == 0)
        {
            if (_recvBuffer.Count == 0) return null;
            var frame = _recvBuffer.ToArray();
            _recvBuffer.Clear();
            return frame;
        }

        var delimPos = IndexOf(_recvBuffer, FrameDelimiter);
        if (delimPos < 0) return null;

        var complete = _recvBuffer.Take(delimPos).ToArray();
        _recvBuffer.RemoveRange(0, delimPos + FrameDelimiter.Length);
        return complete;
    }

    private static int IndexOf(List<byte> source, byte[] pattern)
    {
        for (int i = 0; i <= source.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            _stream?.Dispose();
            _client?.Dispose();
            _recvBuffer.Clear();
            _client = new TcpClient();
            await _client.ConnectAsync(Host, Port, ct);
            _stream = _client.GetStream();
            _client.ReceiveTimeout = ReceiveTimeoutMs;
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
        _reconnectLock.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        _reconnectLock.Dispose();
    }
}
