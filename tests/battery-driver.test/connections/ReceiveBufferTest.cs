using battery.driver.connections;

namespace battery.driver.test.connections;

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
}
