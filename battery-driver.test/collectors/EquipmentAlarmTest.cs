using battery.driver.collectors;
using battery.driver.models;

namespace battery.driver.test.collectors;

public class EquipmentAlarmTest
{
    [Fact]
    public void Process_Valid344Frame_ParsesAlarmData()
    {
        var frame = new byte[344];
        frame[0] = 0xFE;
        frame[1] = 0x01; frame[2] = 0x56;
        frame[3] = 0x00; frame[4] = 0x01;
        frame[5] = 0x01;
        frame[6] = 0x01;
        frame[7 + 50] = 0x02;
        frame[7 + 100] = 0x06;
        frame[343] = 0xEE;

        var collector = new EquipmentAlarm();
        AlarmData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
        Assert.Equal(1, result.Value.LeftRight);
        Assert.Equal(336, result.Value.AbnormalFlags.Length);
        Assert.Equal(0x02, result.Value.AbnormalFlags[50]);
        Assert.Equal(0x06, result.Value.AbnormalFlags[100]);
    }

    [Fact]
    public void Process_WrongStartByte_Throws()
    {
        var frame = new byte[344];
        frame[0] = 0xFD;
        var collector = new EquipmentAlarm();
        Assert.Throws<InvalidDataException>(() => collector.Process(frame));
    }
}
