using System.Net;
using System.Net.Sockets;

namespace CollectionDrivers.BatteryDriver.Connections;

/// <summary>
/// 电池设备 TCP 服务端连接。作为 TcpListener 被动接受设备连接，
/// 通过 ReceiveBuffer 拆帧后推送 OnDataReceived 事件。支持心跳超时检测。
/// </summary>
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

    /// <summary>当前是否已连接</summary>
    public bool IsConnected => _client?.Connected ?? false;
    /// <summary>客户端连接建立事件</summary>
    public event Action? OnClientConnected;
    /// <summary>客户端断开事件</summary>
    public event Action? OnClientDisconnected;
    /// <summary>数据帧接收事件</summary>
    public event Action<byte[]>? OnDataReceived;
    /// <summary>错误事件</summary>
    public event Action<Exception, string>? OnError;

    /// <summary>构造 TCP 服务端连接</summary>
    /// <param name="port">监听端口</param>
    /// <param name="heartbeatTimeoutSeconds">心跳超时（秒），0 禁用</param>
    public TcpConnection(int port, int heartbeatTimeoutSeconds = 60)
    {
        _port = port;
        _heartbeatTimeoutSeconds = heartbeatTimeoutSeconds;
        _receiveBuffer.OnFrameReceived += frame => OnDataReceived?.Invoke(frame);
        _receiveBuffer.OnError += (ex, ctx) => OnError?.Invoke(ex, ctx);
    }

    /// <summary>启动监听循环，接受客户端连接</summary>
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

    /// <summary>发送数据到已连接设备（线程安全）</summary>
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

    /// <summary>检查心跳超时。超过 heartbeatTimeoutSeconds 无数据则断开连接。</summary>
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

    /// <summary>停止监听和所有连接</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _client?.Close();
        _stream?.Dispose();
        _listener?.Stop();
        OnClientDisconnected?.Invoke();
    }

    /// <summary>释放所有资源</summary>
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
