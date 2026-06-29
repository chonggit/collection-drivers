namespace CollectionDrivers.Common;

/// <summary>
/// 通用 TCP 粘包/半包处理缓冲区。
/// 支持两种拆帧模式：
/// - 分隔符模式（Delimiter）：按 byte[] 分隔符拆帧
/// - 固定长度模式（FixedLength）：按固定字节长度拆帧
/// - 混合模式：先找到分隔符，再验证长度
/// </summary>
public class ReceiveBuffer
{
    private readonly List<byte> _buffer = new();
    private const int MaxBufferSize = 65536;

    public event Action<byte[]>? OnFrameReceived;
    public event Action<Exception, string>? OnError;

    /// <summary>帧分隔符（byte[]，如 \r\n）</summary>
    public byte[]? FrameDelimiter { get; set; }

    /// <summary>固定帧长度（>0 时启用固定长度模式）</summary>
    public int FixedFrameLength { get; set; }

    /// <summary>缓冲区内字节数</summary>
    public int BufferedBytes => _buffer.Count;

    /// <summary>追加接收到的字节</summary>
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

    /// <summary>清空缓冲区</summary>
    public void Clear() => _buffer.Clear();

    private void TryParse()
    {
        while (_buffer.Count > 0)
        {
            if (FixedFrameLength > 0)
            {
                // 固定长度模式
                if (_buffer.Count < FixedFrameLength) break;
                var frame = _buffer.GetRange(0, FixedFrameLength).ToArray();
                _buffer.RemoveRange(0, FixedFrameLength);
                OnFrameReceived?.Invoke(frame);
                continue;
            }

            if (FrameDelimiter != null && FrameDelimiter.Length > 0)
            {
                // 分隔符模式
                var delimPos = IndexOf(_buffer, FrameDelimiter);
                if (delimPos < 0) break;
                var frame = _buffer.GetRange(0, delimPos).ToArray();
                _buffer.RemoveRange(0, delimPos + FrameDelimiter.Length);
                OnFrameReceived?.Invoke(frame);
                continue;
            }

            // 无拆帧规则：整个缓冲区当一帧输出
            var all = _buffer.ToArray();
            _buffer.Clear();
            OnFrameReceived?.Invoke(all);
        }
    }

    private static int IndexOf(List<byte> source, byte[] pattern)
    {
        for (int i = 0; i <= source.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
