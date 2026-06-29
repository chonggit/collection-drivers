namespace CollectionDrivers.BatteryDriver.Models;

public readonly record struct DriverStatus
{
    public bool IsConnected { get; init; }
    public DateTime? LastDataReceivedAt { get; init; }
    public int PendingCommandCount { get; init; }
}
