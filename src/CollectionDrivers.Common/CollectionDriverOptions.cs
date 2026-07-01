using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动库顶层配置，从宿主 IConfiguration 绑定。
/// </summary>
public class CollectionDriverOptions
{
    /// <summary>机器列表</summary>
    public List<MachineOptions> Machines { get; set; } = new();
}
