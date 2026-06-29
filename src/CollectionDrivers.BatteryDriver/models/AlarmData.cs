namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct AlarmData
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public byte[] AbnormalFlags { get; init; }
    public DateTime Timestamp { get; init; }
}
