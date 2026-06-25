namespace battery.driver.models;

public readonly record struct WarningData
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public WarningChannel[] Channels { get; init; }
    public DateTime Timestamp { get; init; }
}
