namespace battery.driver.models;

public readonly record struct ResultData
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public byte[] ChannelResults { get; init; }
    public DateTime Timestamp { get; init; }
}
