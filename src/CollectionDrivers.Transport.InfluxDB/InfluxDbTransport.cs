using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Microsoft.Extensions.Logging;
using Scriban;

namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB 传输层。将采集数据通过 Line Protocol 写入 InfluxDB。
/// 支持通过 Scriban 模板进行数据变换。
/// </summary>
public class InfluxDbTransport : CollectionDrivers.Common.Transport, IDisposable
{
    /// <summary>
    /// 已编译的 Scriban 模板缓存：模板名称 → 模板。
    /// </summary>
    private readonly Dictionary<string, Template> _templateLookup = new();

    private InfluxDBClient _client = null!;
    private WriteApiAsync _writeApi = null!;

    /// <summary>
    /// 配置中的变换器映射：键 → Scriban 模板文本。
    /// </summary>
    private Dictionary<string, string> _transformLookup = new();

    /// <summary>
    /// 缓存配置项，避免重复从动态对象读取。
    /// </summary>
    private string _bucket = string.Empty;
    private string _org = string.Empty;

    public InfluxDbTransport(CollectionDrivers.Common.Machine machine) : base(machine)
    {
    }

    /// <summary>
    /// 初始化 InfluxDB 客户端，解析变换模板配置。
    /// </summary>
    public override async Task CreateAsync()
    {
        var transportCfg = Machine.Configuration.transport;

        _bucket = transportCfg.ContainsKey("bucket") ? (string)transportCfg["bucket"] : "default";
        _org = transportCfg.ContainsKey("org") ? (string)transportCfg["org"] : "default";

        var host = transportCfg.ContainsKey("host") ? (string)transportCfg["host"] : "http://localhost:8086";
        var token = transportCfg.ContainsKey("token") ? (string)transportCfg["token"] : string.Empty;

        Logger.LogInformation("[{MachineId}] Creating InfluxDB client: {Host}, bucket={Bucket}, org={Org}",
            Machine.Id, host, _bucket, _org);

        _client = InfluxDBClientFactory.Create(host, token);
        _writeApi = _client.GetWriteApiAsync();

        if (transportCfg.ContainsKey("transformers") && transportCfg["transformers"] != null)
        {
            _transformLookup = (transportCfg["transformers"] as IDictionary<object, object>)
                ?.ToDictionary(kv => (string)kv.Key, kv => (string)kv.Value)
                ?? new Dictionary<string, string>();

            Logger.LogInformation("[{MachineId}] Loaded {Count} transformer(s)", Machine.Id, _transformLookup.Count);
        }

        return;
    }

    /// <summary>
    /// 根据事件类型将数据写入 InfluxDB。
    /// </summary>
    /// <param name="eventName">事件名称，如 "SWEEP_END"</param>
    /// <param name="payload">事件负载数据</param>
    public override async Task SendAsync(string eventName, dynamic? payload)
    {
        switch (eventName)
        {
            case "SWEEP_END":
                await HandleSweepEndAsync(payload!);
                break;
        }
    }

    /// <summary>
    /// 处理采集周期结束事件。使用类型安全的 SweepEndPayload 替代 dynamic 访问。
    /// 模板变量保持向后兼容：data.observation / data.state.data。
    /// </summary>
    private async Task HandleSweepEndAsync(CollectionDrivers.Common.SweepEndPayload data)
    {
        if (!HasTransform("SWEEP_END")) return;

        var template = _templateLookup["SWEEP_END"];
        // 构造与旧匿名类型兼容的模板变量形状
        var lp = template.Render(new
        {
            data = new
            {
                observation = data.Observation,
                state = new { data = new { online = data.Online, healthy = data.Healthy } }
            }
        });

        if (string.IsNullOrEmpty(lp)) return;

        Logger.LogDebug("[{MachineId}] Writing SWEEP_END: {LineProtocol}", Machine.Id, lp);
        await _writeApi.WriteRecordAsync(lp, WritePrecision.Ms, _bucket, _org);
    }

    /// <summary>
    /// 检查并缓存指定名称的变换模板。
    /// </summary>
    private bool HasTransform(string templateName)
    {
        // 已缓存
        if (_templateLookup.ContainsKey(templateName))
            return true;

        // 配置中存在，编译并缓存
        if (_transformLookup.TryGetValue(templateName, out var transformText))
        {
            var template = Template.Parse(transformText);
            if (template.HasErrors)
            {
                Logger.LogError("[{MachineId}] '{TemplateName}' template transform has errors: {Errors}",
                    Machine.Id, templateName,
                    string.Join("; ", template.Messages));
                return false;
            }

            _templateLookup[templateName] = template;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 释放 InfluxDB 客户端资源。
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
    }
}
