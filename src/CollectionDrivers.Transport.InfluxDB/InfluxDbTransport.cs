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
    private readonly InfluxDbTransportOptions? _options;

    /// <summary>
    /// 配置中的变换器映射：键 → Scriban 模板文本。
    /// </summary>
    private Dictionary<string, string> _transformLookup = new();

    /// <summary>
    /// 缓存配置项，避免重复从动态对象读取。
    /// </summary>
    private string _bucket = string.Empty;
    private string _org = string.Empty;

    /// <summary>DI 构造函数：ILogger + Machine + Transport Options。</summary>
    public InfluxDbTransport(
        ILogger? logger,
        CollectionDrivers.Common.Machine machine,
        InfluxDbTransportOptions options) : base(logger, machine)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 初始化 InfluxDB 客户端，解析变换模板配置。
    /// </summary>
    public override async Task CreateAsync()
    {
        if (_options != null)
        {
            _bucket = _options.Bucket;
            _org = _options.Org;
            _transformLookup = _options.Transformers;

            Logger.LogInformation("[{MachineId}] Creating InfluxDB client: {Host}, bucket={Bucket}, org={Org}",
                Machine.Id, _options.Host, _bucket, _org);

            _client = InfluxDBClientFactory.Create(_options.Host, _options.Token);
            _writeApi = _client.GetWriteApiAsync();
        }
    }
    /// <summary>
    /// 根据事件类型将数据写入 InfluxDB。
    /// </summary>
    /// <param name="eventName">事件名称，如 "SWEEP_END"</param>
    /// <param name="payload">事件负载数据</param>
    public override async Task SendAsync(string eventName, object? payload)
    {
        switch (eventName)
        {
            case "SWEEP_END" when payload is CollectionDrivers.Common.SweepEndPayload data:
                await HandleSweepEndAsync(data);
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
