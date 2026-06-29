using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver.Test.Collectors;

public class ChannelDataTest
{
    [Fact]
    public void Process_Valid2696Frame_ParsesChannelRealData()
    {
        var frame = new byte[2696];
        frame[0] = 0xFD;
        frame[1] = 0x0A; frame[2] = 0x86;
        frame[3] = 0x00; frame[4] = 0x01;
        frame[5] = 0x01;
        frame[6] = 0x01;
        byte[] v0 = BitConverter.GetBytes(3.7f);
        Array.Copy(v0, 0, frame, 7, 4);
        byte[] c0 = BitConverter.GetBytes(1.2f);
        Array.Copy(c0, 0, frame, 1351, 4);
        frame[2695] = 0xED;

        var collector = new ChannelData();
        ChannelRealData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
        Assert.Equal(1, result.Value.LeftRight);
        Assert.Equal(336, result.Value.Voltage.Length);
        Assert.Equal(336, result.Value.Current.Length);
        Assert.Equal(3.7f, result.Value.Voltage[0], 3);
        Assert.Equal(1.2f, result.Value.Current[0], 3);
    }

    [Fact]
    public void Process_WrongStartByte_Throws()
    {
        var frame = new byte[2696];
        frame[0] = 0xFE;

        var collector = new ChannelData();
        Assert.Throws<InvalidDataException>(() => collector.Process(frame));
    }
}
