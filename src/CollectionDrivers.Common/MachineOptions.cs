using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace CollectionDrivers.Common;

/// <summary>
/// 单台机器的配置。
/// </summary>
public class MachineOptions
{
    /// <summary>机器标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Machine 具体类型全名。
    /// 若为 null 则默认使用 Machine 基类（推荐）。
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 驱动标识符。与 Add*Driver 注册时的 DriverId 对应。
    /// 如 "Battery"、"Fins"、"OpcUa"、"Scanner"。
    /// </summary>
    public string? DriverId { get; set; }

    /// <summary>采集间隔（毫秒），对应旧 type.sweep_ms</summary>
    public int SweepMs { get; set; } = 5000;

    /// <summary>
    /// 当前机器对应的 IConfiguration 段引用。
    /// 不通过 .Bind() 填充——由 DriverHostService 在运行时手动注入。
    /// </summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public IConfiguration? Configuration { get; set; }
}
