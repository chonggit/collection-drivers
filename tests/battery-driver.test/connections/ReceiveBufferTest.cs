using CollectionDrivers.BatteryDriver.Connections;

namespace CollectionDrivers.BatteryDriver.Test.Connections;

public class ReceiveBufferTest
{
    [Fact]
    public void Append_ShortBuffer_DoesNotEmitFrame()
    {
        var buf = new ReceiveBuffer();
        int frameCount = 0;
        buf.OnFrameReceived += _ => frameCount++;

        buf.Append(new byte[] { 0xFF, 0x00, 0x05, 0x00, 0x01 }); // only 5 bytes, need 7
        Assert.Equal(0, frameCount);
    }

    [Fact]
    public void Append_Valid7ByteAck_EmitFrame()
    {
        var buf = new ReceiveBuffer();
        byte[]? received = null;
        buf.OnFrameReceived += frame => received = frame;

        var frame = new byte[] { 0xFF, 0x00, 0x05, 0x00, 0x01, 0x01, 0xEF };
        buf.Append(frame);

        Assert.NotNull(received);
        Assert.Equal(7, received!.Length);
        Assert.Equal(0xFF, received[0]);
        Assert.Equal(0xEF, received[6]);
    }

    [Fact]
    public void Append_MultipleFrames_EmitsAll()
    {
        var buf = new ReceiveBuffer();
        var frames = new List<byte[]>();
        buf.OnFrameReceived += frame => frames.Add(frame);

        var frame1 = new byte[] { 0xFF, 0x00, 0x05, 0x00, 0x01, 0x01, 0xEF };
        var frame2 = new byte[] { 0xFF, 0x00, 0x05, 0x00, 0x02, 0x01, 0xEF };
        var combined = new byte[14];
        Array.Copy(frame1, 0, combined, 0, 7);
        Array.Copy(frame2, 0, combined, 7, 7);
        buf.Append(combined);

        Assert.Equal(2, frames.Count);
    }

    [Fact]
    public void Append_GarbageBetweenFrames_SkipsGarbage()
    {
        var buf = new ReceiveBuffer();
        var frames = new List<byte[]>();
        buf.OnFrameReceived += frame => frames.Add(frame);

        var garbageAndFrame = new byte[] { 0xAA, 0xBB, 0xFF, 0x00, 0x05, 0x00, 0x01, 0x01, 0xEF };
        buf.Append(garbageAndFrame);

        var single = Assert.Single(frames);
        Assert.Equal(7, single.Length);
        Assert.Equal(0xFF, single[0]);
    }

    [Fact]
    public void Append_WrongStartByte_NoFrame()
    {
        var buf = new ReceiveBuffer();
        int frameCount = 0;
        buf.OnFrameReceived += _ => frameCount++;

        buf.Append(new byte[] { 0x01, 0x00, 0x05, 0x00, 0x01, 0x01, 0xEF });
        Assert.Equal(0, frameCount);
    }

    [Fact]
    public void BufferOverflow_ClearsBuffer()
    {
        var buf = new ReceiveBuffer();
        Exception? error = null;
        buf.OnError += (ex, _) => error = ex;

        var large = new byte[70000];
        Array.Fill<byte>(large, 0xAA);
        buf.Append(large);

        Assert.NotNull(error);
        Assert.Contains("overflow", error!.Message);
        Assert.Equal(0, buf.BufferedBytes);
    }

    /// <summary>
    /// Bug #11 复现：m_len 不匹配时，OnError 触发后不应继续发送损坏帧。
    /// 当前代码在 m_len 校验后缺少控制流跳转（continue/return），会穿透执行
    /// 导致 OnFrameReceived 仍被调用，将损坏数据传递给下游 Collector。
    /// </summary>
    [Fact]
    public void Append_MlenMismatch_ReportsErrorButDoesNotEmitFrame()
    {
        var buf = new ReceiveBuffer();
        int frameCount = 0;
        bool errorRaised = false;
        buf.OnFrameReceived += _ => frameCount++;
        buf.OnError += (ex, _) => errorRaised = true;

        // 构造 7 字节帧：start=0xFF, end=0xEF(匹配 CommandAck 定义),
        // 但 m_len = 0x03E7 = 999（正确值应为 5 = 7-2）
        var frame = new byte[] { 0xFF, 0x03, 0xE7, 0x00, 0x01, 0x01, 0xEF };

        buf.Append(frame);

        Assert.True(errorRaised, "m_len 不匹配应触发 OnError");
        Assert.Equal(0, frameCount); // ← 当前代码失败：frameCount == 1
    }
}
