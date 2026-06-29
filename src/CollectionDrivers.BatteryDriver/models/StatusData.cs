namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct StatusData
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public byte[] LayerStates { get; init; }
    public DateTime Timestamp { get; init; }
}
