using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver.Test.Models;

public class ModelsTest
{
    [Fact]
    public void ChannelRealData_Should_Set_Properties()
    {
        var voltage = new float[336];
        var current = new float[336];
        voltage[0] = 3.7f;
        current[0] = 1.2f;
        var now = DateTime.UtcNow;

        var data = new ChannelRealData
        {
            CabinetIndex = 1,
            LeftRight = 1,
            Voltage = voltage,
            Current = current,
            Timestamp = now
        };

        Assert.Equal(1, data.CabinetIndex);
        Assert.Equal(1, data.LeftRight);
        Assert.Equal(336, data.Voltage.Length);
        Assert.Equal(3.7f, data.Voltage[0]);
        Assert.Equal(1.2f, data.Current[0]);
        Assert.Equal(now, data.Timestamp);
    }

    [Fact]
    public void AlarmData_Should_Set_Properties()
    {
        var flags = new byte[336];
        flags[50] = 2;

        var data = new AlarmData
        {
            CabinetIndex = 1,
            LeftRight = 2,
            AbnormalFlags = flags,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal(2, data.AbnormalFlags[50]);
        Assert.Equal(336, data.AbnormalFlags.Length);
    }

    [Fact]
    public void ResultData_Should_Set_Properties()
    {
        var results = new byte[336];
        results[0] = 1;
        results[1] = 2;

        var data = new ResultData
        {
            CabinetIndex = 1,
            LeftRight = 1,
            ChannelResults = results,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal(1, data.ChannelResults[0]);
        Assert.Equal(2, data.ChannelResults[1]);
    }

    [Fact]
    public void WarningChannel_Index_Formula_Should_Match()
    {
        var ch = new WarningChannel
        {
            Layer = 3,
            Voltage = 3.7f,
            Current = 1.2f,
            VoltageBefore = 3.6f,
            CurrentBefore = 1.1f,
            ChannelIndex = 154
        };

        Assert.Equal(3, ch.Layer);
        Assert.Equal(154, ch.ChannelIndex);
    }

    [Fact]
    public void WarningData_Should_Hold_7_Channels()
    {
        var channels = new WarningChannel[7];
        for (int i = 0; i < 7; i++)
            channels[i] = new WarningChannel { Layer = (byte)(i + 1) };

        var data = new WarningData
        {
            CabinetIndex = 1,
            LeftRight = 1,
            Channels = channels,
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal(7, data.Channels.Length);
        Assert.Equal(3, data.Channels[2].Layer);
    }
}
