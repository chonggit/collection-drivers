using System.IO;
using battery.driver.models;

namespace battery.driver.collectors;

public class EquipmentAlarm
{
    public event Action<AlarmData>? OnData;

    public void Process(byte[] frame)
    {
        if (frame.Length < 344)
            throw new InvalidDataException($"EquipmentAlarm frame too short: {frame.Length}");
        if (frame[0] != 0xFE)
            throw new InvalidDataException($"Wrong start byte: 0x{frame[0]:X2}");

        byte cabinetIndex = frame[5];
        byte leftRight = frame[6];

        var abnormalFlags = new byte[336];
        Array.Copy(frame, 7, abnormalFlags, 0, 336);

        var data = new AlarmData
        {
            CabinetIndex = cabinetIndex,
            LeftRight = leftRight,
            AbnormalFlags = abnormalFlags,
            Timestamp = DateTime.UtcNow
        };

        OnData?.Invoke(data);
    }
}
