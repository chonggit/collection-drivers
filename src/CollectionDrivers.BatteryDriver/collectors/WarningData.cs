using System.IO;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver.Collectors;

public class WarningData
{
    public event Action<Models.WarningData>? OnData;

    public void Process(byte[] frame)
    {
        if (frame.Length < 155)
            throw new InvalidDataException($"WarningData frame too short: {frame.Length}");
        if (frame[0] != 0xEA)
            throw new InvalidDataException($"Wrong start byte: 0x{frame[0]:X2}");
        if (frame[154] != 0xED)
            throw new InvalidDataException($"Wrong end byte: 0x{frame[154]:X2}");

        byte cabinetIndex = frame[5];
        byte leftRight = frame[6];

        var channels = new WarningChannel[7];
        for (int i = 0; i < 7; i++)
        {
            int offset = 7 + i * 21;
            channels[i] = new WarningChannel
            {
                Layer = frame[offset],
                Voltage = BitConverter.ToSingle(frame, offset + 1),
                Current = BitConverter.ToSingle(frame, offset + 5),
                VoltageBefore = BitConverter.ToSingle(frame, offset + 9),
                CurrentBefore = BitConverter.ToSingle(frame, offset + 13),
                ChannelIndex = BitConverter.ToInt32(frame, offset + 17)
            };
        }

        OnData?.Invoke(new Models.WarningData
        {
            CabinetIndex = cabinetIndex,
            LeftRight = leftRight,
            Channels = channels,
            Timestamp = DateTime.UtcNow
        });
    }
}
