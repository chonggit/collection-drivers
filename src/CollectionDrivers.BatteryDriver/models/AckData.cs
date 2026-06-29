namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct AckData
{
    public ushort SeqNo { get; init; }
    public byte Status { get; init; }
    public DateTime Timestamp { get; init; }
}
