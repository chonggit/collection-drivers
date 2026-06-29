using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using l99.driver.@base;
using Microsoft.Extensions.Logging;
using Scriban;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Transport.InfluxDB;

/// <summary>
/// InfluxDB 传输层。将采集数据通过 Line Protocol 写入 InfluxDB。
/// 支持通过 Scriban 模板进行数据变换。
/// </summary>
public class InfluxDbTransport : Transport
{
    /// <summary>
    /// 已编译的 Scriban 模板缓存：Veneer 名称 → 模板。
    /// </summary>
    private readonly Dictionary<string, Template> _templateLookup = new();

    private InfluxDBClient _client = null!;
    private WriteApiAsync _writeApi = null!;

    /// <summary>
    /// 配置中的变换器映射：Veneer 类型全名（或特殊名称）→ Scriban 模板文本。
    /// </summary>
    private Dictionary<string, string> _transformLookup = new();

    /// <summary>
    /// 缓存配置项，避免重复从动态对象读取。
    /// </summary>
    private string _bucket = string.Empty;
    private string _org = string.Empty;

    public InfluxDbTransport(Machine machine) : base(machine)
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
    /// </summary>
    /// <param name="parameters">[0]=事件名, [1]=Veneer, [2]=数据</param>
    public override async Task SendAsync(params dynamic[] parameters)
    {
        var @event = (string)parameters[0];
        var veneer = (Veneer)parameters[1];
        var data = parameters[2];

        switch (@event)
        {
            case "DATA_ARRIVE":
                await HandleDataArriveAsync(veneer, data);
                break;

            case "SWEEP_END":
                await HandleSweepEndAsync(data);
                break;

            case "INT_MODEL":
                // 中间模型暂不处理
                break;
        }
    }

    /// <summary>
    /// 处理数据到达事件：按 Veneer 查找变换模板，渲染后写入 InfluxDB。
    /// </summary>
    private async Task HandleDataArriveAsync(Veneer veneer, dynamic data)
    {
        if (!HasTransform(veneer)) return;

        var template = _templateLookup[veneer.Name];
        var lp = template.Render(new { data.observation, data.state.data });

        if (string.IsNullOrEmpty(lp)) return;

        Logger.LogDebug("[{MachineId}] Writing DATA_ARRIVE: {LineProtocol}", Machine.Id, lp);
        await _writeApi.WriteRecordAsync(lp, WritePrecision.Ms, _bucket, _org);
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
    /// <param name="templateName">模板查找名称（对应 Veneer.Name 或 "SWEEP_END" 等）</param>
    /// <param name="transformName">配置中的 Key，默认与 templateName 相同</param>
    private bool HasTransform(string templateName, string? transformName = null)
    {
        transformName ??= templateName;

        // 已缓存
        if (_templateLookup.ContainsKey(templateName))
            return true;

        // 配置中存在，编译并缓存
        if (_transformLookup.TryGetValue(transformName, out var transformText))
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
    /// 检查并缓存指定 Veneer 对应的变换模板。
    /// </summary>
    private bool HasTransform(Veneer veneer)
    {
        return HasTransform(
            veneer.Name,
            $"{veneer.GetType().FullName}, {veneer.GetType().Assembly.GetName().Name}");
    }

    /// <summary>
    /// 释放 InfluxDB 客户端资源。
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
    }
}
