using System.IO;
using CollectionDrivers.BatteryDriver.Models;

namespace CollectionDrivers.BatteryDriver.Collectors;

public class ChannelData
{
    public event Action<ChannelRealData>? OnData;

    public void Process(byte[] frame)
    {
        if (frame.Length < 2696)
            throw new InvalidDataException($"ChannelData frame too short: {frame.Length}");
        if (frame[0] != 0xFD)
            throw new InvalidDataException($"Wrong start byte: 0x{frame[0]:X2}");

        byte cabinetIndex = frame[5];
        byte leftRight = frame[6];

        var voltage = new float[336];
        var current = new float[336];

        for (int i = 0; i < 336; i++)
        {
            voltage[i] = BitConverter.ToSingle(frame, 7 + i * 4);
            current[i] = BitConverter.ToSingle(frame, 7 + 1344 + i * 4);
        }

        var data = new ChannelRealData
        {
            CabinetIndex = cabinetIndex,
            LeftRight = leftRight,
            Voltage = voltage,
            Current = current,
            Timestamp = DateTime.UtcNow
        };

        OnData?.Invoke(data);
    }
}
