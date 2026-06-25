using System.IO;
using battery.driver.models;

namespace battery.driver.collectors;

/// <summary>
/// 命令状态采集器，解析 0xFF 帧为 AckData（7字节）或 StatusData（65字节）
/// </summary>
public class CommandStatus
{
    /// <summary>
    /// 当解析到 ACK 帧时触发
    /// </summary>
    public event Action<AckData>? OnAck;

    /// <summary>
    /// 当解析到状态帧时触发
    /// </summary>
    public event Action<StatusData>? OnState;

    /// <summary>
    /// 处理 ACK 帧（7字节）
    /// </summary>
    public void ProcessAck(byte[] frame)
    {
        if (frame.Length < 7 || frame[0] != 0xFF || frame[6] != 0xEF)
            throw new InvalidDataException("Invalid ACK frame");

        ushort seqNo = (ushort)((frame[3] << 8) | frame[4]);
        byte status = frame[5];

        OnAck?.Invoke(new AckData
        {
            SeqNo = seqNo,
            Status = status,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// 处理状态帧（65字节）
    /// </summary>
    public void ProcessState(byte[] frame)
    {
        if (frame.Length < 65 || frame[0] != 0xFF || frame[64] != 0xEF)
            throw new InvalidDataException("Invalid state frame");

        byte cabinetIndex = frame[5];
        byte leftRight = frame[6];

        var states = new byte[7];
        Array.Copy(frame, 57, states, 0, 7);

        OnState?.Invoke(new StatusData
        {
            CabinetIndex = cabinetIndex,
            LeftRight = leftRight,
            LayerStates = states,
            Timestamp = DateTime.UtcNow
        });
    }
}
