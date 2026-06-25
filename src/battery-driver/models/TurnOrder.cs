namespace battery.driver.models;

public readonly record struct TurnOrder
{
    public byte CabinetIndex { get; init; }
    public byte LeftRight { get; init; }
    public string? Technology { get; init; }
    public byte[] LayerCommands { get; init; }
}
