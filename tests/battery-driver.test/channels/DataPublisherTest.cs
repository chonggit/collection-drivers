using battery.driver.channels;
using battery.driver.models;

namespace battery.driver.test.channels;

public class DataPublisherTest
{
    [Fact]
    public void Publish_ChannelRealData_Fires_Event()
    {
        using var pub = new DataPublisher();
        ChannelRealData? received = null;
        pub.OnChannelData += data => received = data;

        var data = new ChannelRealData { CabinetIndex = 1, Timestamp = DateTime.UtcNow };
        pub.Publish(data);

        Assert.NotNull(received);
        Assert.Equal((byte)1, received.Value.CabinetIndex);
    }

    [Fact]
    public void Publish_ChannelRealData_Writes_To_Channel()
    {
        using var pub = new DataPublisher();
        var data = new ChannelRealData { CabinetIndex = 2, Timestamp = DateTime.UtcNow };
        pub.Publish(data);

        var reader = pub.GetChannelDataReader();
        Assert.True(reader.TryRead(out var read));
        Assert.Equal((byte)2, read.CabinetIndex);
    }

    [Fact]
    public void Publish_Multiple_Types_Works()
    {
        using var pub = new DataPublisher();
        int eventCount = 0;
        pub.OnAlarm += _ => eventCount++;
        pub.OnResult += _ => eventCount++;

        pub.Publish(new AlarmData { Timestamp = DateTime.UtcNow });
        pub.Publish(new ResultData { Timestamp = DateTime.UtcNow });

        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void OnError_Fired_When_Channel_Full()
    {
        using var pub = new DataPublisher();
        Exception? capturedEx = null;
        string? capturedCtx = null;
        pub.OnError += (ex, ctx) => { capturedEx = ex; capturedCtx = ctx; };

        // Fill the bounded channel (AckData size = 200)
        for (int i = 0; i < 1500; i++)
        {
            pub.Publish(new AckData { SeqNo = (ushort)i, Status = 1, Timestamp = DateTime.UtcNow });
        }

        Assert.NotNull(capturedEx);
        Assert.Contains("full", capturedEx!.Message);
        Assert.Equal("DataPublisher", capturedCtx);
    }
}
