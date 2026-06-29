using System.Net;
using System.Net.Sockets;

namespace CollectionDrivers.BatteryDriver.Connections;

public class TcpConnection : IDisposable
{
    private readonly int _port;
    private readonly int _heartbeatTimeoutSeconds;
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly ReceiveBuffer _receiveBuffer = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private DateTime _lastDataTime = DateTime.MinValue;

    public bool IsConnected => _client?.Connected ?? false;
    public event Action? OnClientConnected;
    public event Action? OnClientDisconnected;
    public event Action<byte[]>? OnDataReceived;
    public event Action<Exception, string>? OnError;

    public TcpConnection(int port, int heartbeatTimeoutSeconds = 60)
    {
        _port = port;
        _heartbeatTimeoutSeconds = heartbeatTimeoutSeconds;
        _receiveBuffer.OnFrameReceived += frame => OnDataReceived?.Invoke(frame);
        _receiveBuffer.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                AcceptClient(client);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void AcceptClient(TcpClient client)
    {
        _cts?.Cancel();
        _client?.Close();
        _stream?.Dispose();

        _cts = new CancellationTokenSource();
        _client = client;
        _stream = client.GetStream();
        _lastDataTime = DateTime.UtcNow;
        _receiveBuffer.Clear();

        OnClientConnected?.Invoke();
        _ = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var bytesRead = await _stream.ReadAsync(buffer, ct);
                if (bytesRead == 0) break;

                _lastDataTime = DateTime.UtcNow;
                var segment = new byte[bytesRead];
                Array.Copy(buffer, 0, segment, 0, bytesRead);
                _receiveBuffer.Append(segment);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "TcpConnection.ReceiveLoop");
        }
        finally
        {
            OnClientDisconnected?.Invoke();
        }
    }

    public async Task SendAsync(byte[] data)
    {
        if (_stream == null || !IsConnected)
            throw new InvalidOperationException("Not connected");

        await _sendLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(data);
            await _stream.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void CheckHeartbeat()
    {
        if (_heartbeatTimeoutSeconds <= 0) return;
        if (!IsConnected) return;

        var elapsed = (DateTime.UtcNow - _lastDataTime).TotalSeconds;
        if (elapsed > _heartbeatTimeoutSeconds)
        {
            OnError?.Invoke(new TimeoutException($"Heartbeat timeout: {elapsed:F0}s"), "TcpConnection");
            _cts?.Cancel();
            _client?.Close();
            _stream?.Dispose();
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _client?.Close();
        _stream?.Dispose();
        _listener?.Stop();
        OnClientDisconnected?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _sendLock.Dispose();
        _client?.Dispose();
        _stream?.Dispose();
        _listener?.Stop();
    }
}
