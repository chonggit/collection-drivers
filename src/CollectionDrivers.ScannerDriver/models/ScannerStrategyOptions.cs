namespace CollectionDrivers.ScannerDriver.Models;

/// <summary>
/// 扫描枪策略配置。
/// </summary>
public class ScannerStrategyOptions
{
    /// <summary>主机地址</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>端口号</summary>
    public int Port { get; set; } = 2000;

    /// <summary>工作模式：sync 或 async</summary>
    public string Mode { get; set; } = "sync";

    /// <summary>重试次数</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>连接超时（毫秒）</summary>
    public int ConnectTimeoutMs { get; set; } = 3000;

    /// <summary>接收超时（毫秒）</summary>
    public int ReceiveTimeoutMs { get; set; } = 5000;

    /// <summary>是否启用去重</summary>
    public bool DedupEnabled { get; set; }

    /// <summary>协议配置</summary>
    public ScannerProtocolOptions Protocol { get; set; } = new();
}

/// <summary>扫描枪协议配置</summary>
public class ScannerProtocolOptions
{
    /// <summary>发送命令（十六进制字符串）</summary>
    public string SendCommandHex { get; set; } = "";

    /// <summary>响应编码</summary>
    public string ResponseEncoding { get; set; } = "ascii";

    /// <summary>条码正则表达式</summary>
    public string? BarcodeRegex { get; set; }

    /// <summary>正则匹配组索引</summary>
    public int RegexGroupIndex { get; set; }

    /// <summary>帧分隔符（十六进制字符串）</summary>
    public string? FrameDelimiterHex { get; set; }

    /// <summary>需移除的前缀列表</summary>
    public string[] RemovePrefixes { get; set; } = Array.Empty<string>();

    /// <summary>需移除的后缀列表</summary>
    public string[] RemoveSuffixes { get; set; } = Array.Empty<string>();
}
