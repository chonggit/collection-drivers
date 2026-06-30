using System.Reflection;
using CollectionDrivers.Common;

namespace CollectionDrivers.BatteryDriver.Test.Connections;

/// <summary>
/// Bug #10 验证：TcpClientConnection.ReceiveLoop 每次重连通过 += 注册处理器，
/// 但从不断开 -=。正确行为：处理器在 StartReceiveLoop 中注册一次，不应随重连次数累加。
/// </summary>
public class TcpClientConnectionTest
{
    /// <summary>
    /// RED 测试：验证当前代码中，未建立 TCP 连接时 OnDataReceived 不会被触发。
    /// 根因：ReceiveLoop 中的 += 仅在 TCP 连接成功后执行。
    /// 修复后，处理器在 StartReceiveLoop 中注册，不依赖 TCP 连接状态即可接收数据。
    /// </summary>
    [Fact]
    public void OnDataReceived_FiresAfterStartReceiveLoop_EvenWithoutTcpConnection()
    {
        var conn = new TcpClientConnection();
        conn.Configure("127.0.0.1", 19999); // 一个不会连接的端口
        conn.FrameDelimiter = null; // 无分隔符：整个缓冲区当一帧

        byte[]? received = null;
        conn.OnDataReceived += data => received = data;

        // StartReceiveLoop 应在启动时注册帧处理器（修复后）
        // 当前代码：处理器在 ReceiveLoop 内注册，需 TCP 连接建立后才执行
        conn.StartReceiveLoop();

        // 给后台任务一点时间启动（不需要连接成功）
        Thread.Sleep(200);

        // 直接向内部 ReceiveBuffer 注入一帧数据
        var testFrame = new byte[] { 0x01, 0x02, 0x03 };
        conn._receiveBuffer.Append(testFrame);

        Thread.Sleep(100);

        // RED: 当前代码中 received == null（处理器未注册，因为 TCP 连接未建立）
        // GREEN: 修复后 received 不为 null == testFrame
        Assert.NotNull(received);
        Assert.Equal(testFrame, received);
    }

    /// <summary>
    /// 验证修复后：多次重连不会导致 OnDataReceived 重复触发。
    /// 通过验证 StartReceiveLoop 仅注册一次处理器来保证。
    /// </summary>
    [Fact]
    public void OnDataReceived_FiresExactlyOncePerFrame_AfterStartReceiveLoop()
    {
        var conn = new TcpClientConnection();
        conn.Configure("127.0.0.1", 19999);
        conn.FrameDelimiter = null;

        int fireCount = 0;
        conn.OnDataReceived += _ => fireCount++;

        conn.StartReceiveLoop();
        Thread.Sleep(200);

        conn._receiveBuffer.Append(new byte[] { 0x01 });

        Thread.Sleep(100);

        Assert.Equal(1, fireCount);
    }

    // ============================================================
    // 发现3 复现：DisposeAsync 应在 Dispose _reconnectLock 之前等待信号量释放
    // ============================================================

    /// <summary>
    /// RED: 当前 DisposeAsync 直接 Dispose _reconnectLock 而不等待。
    /// 如果 ReconnectAsync 持有锁，其 finally 块 Release 已释放的 SemaphoreSlim → ObjectDisposedException。
    /// GREEN: DisposeAsync 应先等待获取锁，确保无并发操作后再 Dispose。
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WaitsForReconnectLock_BeforeDisposing()
    {
        var conn = new TcpClientConnection();
        conn.Configure("127.0.0.1", 19999);

        // 通过反射获取私有 _reconnectLock
        var lockField = typeof(TcpClientConnection).GetField("_reconnectLock",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lockField);
        var reconnectLock = (SemaphoreSlim)lockField!.GetValue(conn)!;

        // 模拟 ReconnectAsync 进行中：先持有信号量
        await reconnectLock.WaitAsync();

        // 在后台启动 DisposeAsync
        var disposeTask = conn.DisposeAsync().AsTask();

        // DisposeAsync 应该被阻塞，等待信号量释放
        await Task.Delay(300);
        Assert.False(disposeTask.IsCompleted,
            "RED: DisposeAsync 应等待 _reconnectLock 释放后再继续（当前直接 Dispose 信号量，不会阻塞）");

        // 释放信号量（模拟 ReconnectAsync 的 finally 块）
        reconnectLock.Release();

        // 现在 DisposeAsync 应该能完成
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
        var completed = await Task.WhenAny(disposeTask, timeoutTask);
        Assert.Equal(disposeTask, completed);

        // 验证信号量已被释放（ObjectDisposedException）
        Assert.Throws<ObjectDisposedException>(() => reconnectLock.Wait(TimeSpan.Zero));
    }
}
