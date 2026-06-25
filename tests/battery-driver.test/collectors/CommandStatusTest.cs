using battery.driver.collectors;
using battery.driver.models;

namespace battery.driver.test.collectors;

public class CommandStatusTest
{
    [Fact]
    public void ProcessAck_7ByteFrame_ParsesAckData()
    {
        var frame = new byte[7];
        frame[0] = 0xFF;
        frame[1] = 0x00; frame[2] = 0x05;
        frame[3] = 0x00; frame[4] = 0x0A;
        frame[5] = 0x01;
        frame[6] = 0xEF;

        var collector = new CommandStatus();
        AckData? ackResult = null;
        StatusData? stateResult = null;
        collector.OnAck += data => ackResult = data;
        collector.OnState += data => stateResult = data;

        collector.ProcessAck(frame);

        Assert.NotNull(ackResult);
        Assert.Equal((ushort)10, ackResult.Value.SeqNo);
        Assert.Equal(1, ackResult.Value.Status);
        Assert.Null(stateResult);
    }

    [Fact]
    public void ProcessState_65ByteFrame_ParsesStatusData()
    {
        var frame = new byte[65];
        frame[0] = 0xFF;
        frame[1] = 0x00; frame[2] = 0x3F;
        frame[3] = 0x00; frame[4] = 0x01;
        frame[5] = 0x02;
        frame[6] = 0x01;
        frame[57] = 0x03;
        frame[58] = 0x03;
        frame[59] = 0x04;
        frame[64] = 0xEF;

        var collector = new CommandStatus();
        AckData? ackResult = null;
        StatusData? stateResult = null;
        collector.OnAck += data => ackResult = data;
        collector.OnState += data => stateResult = data;

        collector.ProcessState(frame);

        Assert.NotNull(stateResult);
        Assert.Equal(2, stateResult.Value.CabinetIndex);
        Assert.Equal(1, stateResult.Value.LeftRight);
        Assert.Equal(7, stateResult.Value.LayerStates.Length);
        Assert.Equal(3, stateResult.Value.LayerStates[0]);
        Assert.Equal(3, stateResult.Value.LayerStates[1]);
        Assert.Equal(4, stateResult.Value.LayerStates[2]);
        Assert.Null(ackResult);
    }
}
