using System.IO;

namespace battery.driver.connections;

public class ReceiveBuffer
{
    private readonly List<byte> _buffer = new();
    private const int MaxBufferSize = 65536;

    public event Action<byte[]>? OnFrameReceived;
    public event Action<Exception, string>? OnError;

    // Frame definitions from spec §3.2 (tried in order: shortest first per start byte)
    private static readonly FrameDef[] FrameDefs =
    {
        // 0xFF sub-types (tried in order: shortest first)
        new(0xFF, 7,   0xEF, "CommandAck"),
        new(0xFF, 65,  0xEF, "CommandState"),
        new(0xFF, 344, 0xEF, "CommandResult"),
        // 0xFE
        new(0xFE, 344, 0xEE, "EquipmentAlarm"),
        // 0xFD
        new(0xFD, 2696, 0xED, "ChannelData"),
        // 0xEA
        new(0xEA, 155, 0xED, "WarningData"),
    };

    public void Append(byte[] segment)
    {
        _buffer.AddRange(segment);

        if (_buffer.Count > MaxBufferSize)
        {
            OnError?.Invoke(new InvalidOperationException("ReceiveBuffer overflow"), "ReceiveBuffer");
            _buffer.Clear();
            return;
        }

        TryParse();
    }

    private void TryParse()
    {
        while (_buffer.Count >= 7)
        {
            var startByte = _buffer[0];
            var candidates = FrameDefs.Where(f => f.StartByte == startByte).ToArray();

            bool parsed = false;
            foreach (var def in candidates)
            {
                if (_buffer.Count < def.TotalLength)
                    continue;

                if (_buffer[def.TotalLength - 1] != def.EndByte)
                    continue;

                // Verify m_len field (total_len - 2)
                int mLen = (_buffer[1] << 8) | _buffer[2];
                if (mLen != def.TotalLength - 2)
                {
                    OnError?.Invoke(
                        new InvalidDataException(
                            $"m_len mismatch: expected {def.TotalLength - 2}, got {mLen}"),
                        $"ReceiveBuffer.{def.Name}");
                }

                var frame = _buffer.GetRange(0, def.TotalLength).ToArray();
                _buffer.RemoveRange(0, def.TotalLength);
                OnFrameReceived?.Invoke(frame);
                parsed = true;
                break;
            }

            if (!parsed)
            {
                // Garbage byte recovery: advance one byte
                _buffer.RemoveAt(0);
            }
        }
    }

    public void Clear() => _buffer.Clear();
    public int BufferedBytes => _buffer.Count;

    private readonly record struct FrameDef(byte StartByte, int TotalLength, byte EndByte, string Name);
}
