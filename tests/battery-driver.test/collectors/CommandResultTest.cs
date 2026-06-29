using CollectionDrivers.BatteryDriver.Collectors;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver.Test.Collectors;

public class CommandResultTest
{
    [Fact]
    public void Process_Valid344ResultFrame_ParsesResultData()
    {
        var frame = new byte[344];
        frame[0] = 0xFF;
        frame[1] = 0x01; frame[2] = 0x56;
        frame[3] = 0x00; frame[4] = 0x01;
        frame[5] = 0x01;
        frame[6] = 0x02;
        frame[7 + 0] = 0x01;
        frame[7 + 1] = 0x02;
        frame[7 + 2] = 0x03;
        frame[343] = 0xEF;

        var collector = new CommandResult();
        ResultData? result = null;
        collector.OnData += data => result = data;

        collector.Process(frame);

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.CabinetIndex);
        Assert.Equal(2, result.Value.LeftRight);
        Assert.Equal(1, result.Value.ChannelResults[0]);
        Assert.Equal(2, result.Value.ChannelResults[1]);
        Assert.Equal(3, result.Value.ChannelResults[2]);
    }
}
