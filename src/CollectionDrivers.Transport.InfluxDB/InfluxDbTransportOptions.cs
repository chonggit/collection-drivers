namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB 传输层配置。
/// </summary>
public class InfluxDbTransportOptions
{
    /// <summary>InfluxDB 主机地址</summary>
    public string Host { get; set; } = "http://localhost:8086";

    /// <summary>认证 Token</summary>
    public string Token { get; set; } = "";

    /// <summary>Bucket 名称</summary>
    public string Bucket { get; set; } = "default";

    /// <summary>组织名称</summary>
    public string Org { get; set; } = "default";

    /// <summary>Scriban 模板变换器映射：模板名 → 模板文本</summary>
    public Dictionary<string, string> Transformers { get; set; } = new();
}
