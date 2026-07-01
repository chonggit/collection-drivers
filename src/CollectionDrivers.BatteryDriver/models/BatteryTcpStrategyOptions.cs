namespace CollectionDrivers.BatteryDriver.Models;

/// <summary>
/// Battery TCP 策略配置。
/// </summary>
public class BatteryTcpStrategyOptions
{
    /// <summary>数据端口</summary>
    public int Port { get; set; } = 13000;

    /// <summary>告警端口</summary>
    public int WarningPort { get; set; } = 13100;

    /// <summary>心跳超时（秒）</summary>
    public int HeartbeatTimeoutS { get; set; } = 60;
}
