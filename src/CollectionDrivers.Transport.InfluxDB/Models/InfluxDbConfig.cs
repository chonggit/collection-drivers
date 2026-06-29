namespace CollectionDrivers.Transport.InfluxDB.Models;

/// <summary>
/// InfluxDB 传输层配置模型。
/// </summary>
public class InfluxDbConfig
{
    /// <summary>
    /// InfluxDB 服务器地址，例如 http://localhost:8086。
    /// </summary>
    public string Host { get; set; } = "http://localhost:8086";

    /// <summary>
    /// 认证令牌。
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// 目标存储桶名称。
    /// </summary>
    public string Bucket { get; set; } = "default";

    /// <summary>
    /// 目标组织名称。
    /// </summary>
    public string Org { get; set; } = "default";

    /// <summary>
    /// 数据变换模板。
    /// Key 为 Veneer 类型全名（或特殊名称如 "SWEEP_END"），
    /// Value 为 Scriban 模板文本，用于将采集数据渲染为 InfluxDB Line Protocol。
    /// </summary>
    public Dictionary<string, string> Transformers { get; set; } = new();
}
