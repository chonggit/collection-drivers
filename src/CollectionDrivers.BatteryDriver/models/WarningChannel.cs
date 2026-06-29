namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct WarningChannel
{
    public byte Layer { get; init; }
    public float Voltage { get; init; }
    public float Current { get; init; }
    public float VoltageBefore { get; init; }
    public float CurrentBefore { get; init; }
    public int ChannelIndex { get; init; }
}
