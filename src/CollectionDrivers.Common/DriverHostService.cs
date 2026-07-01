using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动宿主服务。通过 IConfiguration 接收宿主配置，
/// 使用 IMachineScopeFactory 为每台机器创建独立 Scope 并启动采集循环。
/// </summary>
public class DriverHostService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly IOptions<CollectionDriverOptions> _options;
    private readonly IMachineScopeFactory _scopeFactory;
    private readonly ILogger<DriverHostService> _logger;

    /// <summary>构造函数注入</summary>
    public DriverHostService(
        IConfiguration config,
        IOptions<CollectionDriverOptions> options,
        IMachineScopeFactory scopeFactory,
        ILogger<DriverHostService> logger)
    {
        _config = config;
        _options = options;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var machines = _options.Value.Machines.Where(m => m.Enabled).ToList();
        _logger.LogInformation("Starting {Count} machine(s)", machines.Count);

        var rootSection = _config.GetSection("CollectionDrivers");
        var tasks = new List<Task>();

        for (int i = 0; i < _options.Value.Machines.Count; i++)
        {
            var machineCfg = _options.Value.Machines[i];
            if (!machineCfg.Enabled) continue;

            // 填充 IConfiguration 引用——连接 IOptions 和 IConfiguration 的关键桥接
            machineCfg.Configuration = rootSection.GetSection($"Machines:{i}");

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await using var scope = _scopeFactory.CreateScope(machineCfg);
                    await scope.RunAsync(ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[{MachineId}] Machine crashed", machineCfg.Id);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
    }
}
