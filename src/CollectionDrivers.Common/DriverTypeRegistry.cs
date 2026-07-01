using Microsoft.Extensions.Logging;

namespace CollectionDrivers.Common;

/// <summary>
/// 驱动类型注册表（Singleton）。各驱动的 Add 扩展方法在此注册类型元数据。
/// MachineScope 创建时通过 MachineOptions.DriverId 查找匹配条目。
/// </summary>
public class DriverTypeRegistry
{
    /// <summary>已注册的驱动条目列表</summary>
    public readonly List<Entry> Entries = new();

    /// <summary>
    /// 按 DriverId 精确匹配注册条目。若 driverId 为 null，返回第一个条目。
    /// 重复 DriverId 注册时 first-wins。
    /// </summary>
    public Entry? Find(string? driverId)
    {
        if (driverId != null)
            return Entries.FirstOrDefault(e => e.DriverId == driverId);
        return Entries.FirstOrDefault();
    }

    /// <summary>驱动类型注册条目</summary>
    public sealed record Entry(
        /// <summary>驱动标识符。Add*Driver 扩展方法设置，用于多驱动区分。</summary>
        string DriverId,
        /// <summary>Machine 具体类型</summary>
        Type MachineType,
        /// <summary>Strategy 具体类型</summary>
        Type StrategyType,
        /// <summary>Handler 具体类型</summary>
        Type HandlerType,
        /// <summary>Strategy Options 类型</summary>
        Type StrategyOptionsType,
        /// <summary>Transport 具体类型（可为 null，表示不创建 Transport）</summary>
        Type? TransportType,
        /// <summary>Transport Options 类型（可为 null）</summary>
        Type? TransportOptionsType
    );
}
