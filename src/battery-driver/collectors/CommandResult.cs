using System.IO;
using battery.driver.models;

namespace battery.driver.collectors;

public class CommandResult
{
    public event Action<ResultData>? OnData;

    public void Process(byte[] frame)
    {
        if (frame.Length < 344)
            throw new InvalidDataException($"CommandResult frame too short: {frame.Length}");
        if (frame[0] != 0xFF)
            throw new InvalidDataException($"Wrong start byte: 0x{frame[0]:X2}");
        if (frame[343] != 0xEF)
            throw new InvalidDataException($"Wrong end byte: 0x{frame[343]:X2}");

        byte cabinetIndex = frame[5];
        byte leftRight = frame[6];

        var results = new byte[336];
        Array.Copy(frame, 7, results, 0, 336);

        var data = new ResultData
        {
            CabinetIndex = cabinetIndex,
            LeftRight = leftRight,
            ChannelResults = results,
            Timestamp = DateTime.UtcNow
        };

        OnData?.Invoke(data);
    }
}
