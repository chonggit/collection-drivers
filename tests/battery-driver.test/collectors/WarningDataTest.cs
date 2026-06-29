namespace CollectionDrivers.BatteryDriver.Test.Collectors;

public class WarningDataTest
{
    [Fact]
    public void Process_Valid155Frame_ParsesWarningData()
    {
        var frame = new byte[155];
        frame[0] = 0xEA;
        frame[1] = 0x00; frame[2] = 0x99;
        frame[3] = 0x00; frame[4] = 0x01;
        frame[5] = 0x01;
        frame[6] = 0x01;

        // First WarningSub at bytes 7-27
        frame[7] = 0x01; // Layer = 1
        BitConverter.GetBytes(3.7f).CopyTo(frame, 8);
        BitConverter.GetBytes(1.2f).CopyTo(frame, 12);
        BitConverter.GetBytes(3.6f).CopyTo(frame, 16);
        BitConverter.GetBytes(1.1f).CopyTo(frame, 20);
        BitConverter.GetBytes(100).CopyTo(frame, 24);

        // Second WarningSub at bytes 28-48
        frame[28] = 0x02; // Layer = 2
        BitConverter.GetBytes(4.0f).CopyTo(frame, 29);

        frame[154] = 0xED;

        var collector = new global::CollectionDrivers.BatteryDriver.Collectors.WarningData();
        CollectionDrivers.BatteryDriver.Models.WarningData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
        Assert.Equal(7, result.Value.Channels.Length);
        Assert.Equal(1, result.Value.Channels[0].Layer);
        Assert.Equal(3.7f, result.Value.Channels[0].Voltage, 3);
        Assert.Equal(2, result.Value.Channels[1].Layer);
        Assert.Equal(4.0f, result.Value.Channels[1].Voltage, 3);
    }
}
