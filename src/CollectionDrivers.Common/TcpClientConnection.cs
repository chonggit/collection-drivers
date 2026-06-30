using System.Net.Sockets;

namespace CollectionDrivers.Common;

/// <summary>
/// 通用 TCP 客户端连接。支持同步请求/响应和异步事件推送两种模式。
/// 内置自动重连、帧拆包（通过 ReceiveBuffer）和接收缓冲区溢出保护。
/// </summary>
public class TcpClientConnection : IDisposable, IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _disposeCts;
    private Task? _receiveTask;
    internal readonly ReceiveBuffer _receiveBuffer = new();
    private const int MaxReceiveBuffer = 65536;
    private volatile bool _disposed;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    /// <summary>目标主机名或 IP</summary>
    public string Host { get; private set; } = "";
    /// <summary>目标端口</summary>
    public int Port { get; private set; }
    /// <summary>连接超时（毫秒）</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;
    /// <summary>接收超时（毫秒）</summary>
    public int ReceiveTimeoutMs { get; set; } = 5000;
    /// <summary>当前是否已连接</summary>
    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>连接建立事件</summary>
    public event Action? OnConnected;
    /// <summary>连接断开事件</summary>
    public event Action? OnDisconnected;
    /// <summary>错误事件（异常 + 上下文）</summary>
    public event Action<Exception, string>? OnError;
    /// <summary>数据帧接收事件</summary>
    public event Action<byte[]>? OnDataReceived;

    /// <summary>帧分隔符（byte[]，如 0x0A=\n），委托给内部 ReceiveBuffer</summary>
    public byte[]? FrameDelimiter
    {
        get => _receiveBuffer.FrameDelimiter;
        set => _receiveBuffer.FrameDelimiter = value;
    }

    /// <summary>配置连接参数（需在 StartReceiveLoop 或 ConnectAsync 之前调用）</summary>
    public void Configure(string host, int port,
        int connectTimeoutMs = 3000, int receiveTimeoutMs = 5000)
    {
        Host = host;
        Port = port;
        ConnectTimeoutMs = connectTimeoutMs;
        ReceiveTimeoutMs = receiveTimeoutMs;
    }

    /// <summary>建立 TCP 连接</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

        _client?.Dispose();
        _client = new TcpClient();
        await _client.ConnectAsync(Host, Port, linkedCts.Token);
        _stream = _client.GetStream();
        _client.ReceiveTimeout = ReceiveTimeoutMs;
        _receiveBuffer.Clear();
        OnConnected?.Invoke();
    }

    /// <summary>
    /// 发送命令并等待完整响应帧。支持自动重连和重试。
    /// ct 仅控制信号量等待和重试取消，读写超时由 ReceiveTimeoutMs + 外部 CTS 控制。
    /// </summary>
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

                // 等待完整帧
                byte[]? result = null;
                void handler(byte[] frame) { result = frame; }
                _receiveBuffer.OnFrameReceived += handler;

                try
                {
                    while (!ct.IsCancellationRequested && result == null)
                    {
                        var available = _client!.Available;
                        if (available > 0)
                        {
                            var buf = new byte[available];
                            var len = await _stream.ReadAsync(buf, 0, buf.Length, ct);
                            _receiveBuffer.Append(buf.Take(len).ToArray());

                            if (_receiveBuffer.BufferedBytes > MaxReceiveBuffer)
                            {
                                _receiveBuffer.Clear();
                                throw new InvalidOperationException("ReceiveBuffer overflow");
                            }
                        }
                        else
                        {
                            await Task.Delay(10, ct);
                        }
                    }
                }
                finally
                {
                    _receiveBuffer.OnFrameReceived -= handler;
                }

                if (result != null) return result;
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

    /// <summary>启动异步接收循环。连接断开时自动重连（指数退避）。帧通过 OnDataReceived 事件推送。</summary>
    public void StartReceiveLoop()
    {
        _disposeCts = new CancellationTokenSource();
        var token = _disposeCts.Token;

        // 注册帧处理器（仅一次，不随重连次数累加）
        _receiveBuffer.OnFrameReceived += frame => OnDataReceived?.Invoke(frame);

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

                _receiveBuffer.Append(buffer.Take(len).ToArray());

                if (_receiveBuffer.BufferedBytes > MaxReceiveBuffer)
                {
                    OnError?.Invoke(
                        new InvalidOperationException("ReceiveBuffer overflow"), "ReceiveLoop");
                    _receiveBuffer.Clear();
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

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _reconnectLock.WaitAsync(ct);
        try
        {
            _stream?.Dispose();
            _client?.Dispose();
            _receiveBuffer.Clear();
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

    /// <summary>同步释放资源。优先使用 DisposeAsync 以等待后台任务安全退出。</summary>
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

    /// <summary>
    /// 异步释放资源。取消后台接收循环并等待其安全退出后再释放网络资源。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts?.Cancel();

        // 等待后台接收任务退出后再释放资源
        if (_receiveTask != null)
        {
            try { await _receiveTask; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
        }

        // 等待重连锁释放后再 Dispose，避免持有锁的线程在 finally 中 Release 时抛出 ObjectDisposedException
        await _reconnectLock.WaitAsync(TimeSpan.FromSeconds(5));
        _stream?.Dispose();
        _client?.Dispose();
        _reconnectLock.Dispose();
    }
}
