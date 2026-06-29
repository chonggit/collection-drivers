namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct ChannelRealData
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public float[] Voltage { get; init; }
    public float[] Current { get; init; }
    public DateTime Timestamp { get; init; }
}
