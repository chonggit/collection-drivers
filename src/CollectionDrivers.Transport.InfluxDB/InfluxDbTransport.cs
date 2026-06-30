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
    public override async Task<dynamic?> CreateAsync()
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

        return null;
    }

    /// <summary>
    /// InfluxDB 客户端在内部管理连接，此处无需额外操作。
    /// </summary>
    public override async Task ConnectAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 根据事件类型将数据写入 InfluxDB。
    /// 当前仅处理 SWEEP_END 事件。
    /// </summary>
    /// <param name="parameters">[0]=事件名, [1]=保留(null), [2]=payload</param>
    public override async Task SendAsync(params dynamic[] parameters)
    {
        var @event = (string)parameters[0];
        var data = parameters[2];

        switch (@event)
        {
            case "SWEEP_END":
                await HandleSweepEndAsync(data);
                break;

            case "INT_MODEL":
                // 中间模型暂不处理
                break;
        }
    }

    /// <summary>
    /// 处理采集周期结束事件：若有 "SWEEP_END" 变换模板则渲染并写入。
    /// </summary>
    private async Task HandleSweepEndAsync(dynamic data)
    {
        if (!HasTransform("SWEEP_END")) return;

        var template = _templateLookup["SWEEP_END"];
        var lp = template.Render(new { data.observation, data.state.data });

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
