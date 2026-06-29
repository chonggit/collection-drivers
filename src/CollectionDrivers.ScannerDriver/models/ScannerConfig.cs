namespace CollectionDrivers.ScannerDriver.Models;

public class ProtocolConfig
{
    public string SendCommandHex { get; set; } = "7374617274";
    public string ResponseEncoding { get; set; } = "ascii";
    public string? BarcodeRegex { get; set; }
    public int RegexGroupIndex { get; set; } = 0;
    public string[]? RemovePrefixes { get; set; }
    public string[]? RemoveSuffixes { get; set; }
    public string? FrameDelimiterHex { get; set; }
}

public class ScannerConfig
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 2001;
    public string Mode { get; set; } = "sync";
    public int RetryCount { get; set; } = 3;
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int ReceiveTimeoutMs { get; set; } = 5000;
    public bool DedupEnabled { get; set; } = true;
    public ProtocolConfig Protocol { get; set; } = new();
}
