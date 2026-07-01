namespace CollectionDrivers.Common;

/// <summary>
/// 创建 MachineScope 的工厂。由 DI 容器注册为 Singleton。
/// </summary>
public interface IMachineScopeFactory
{
    /// <summary>为指定机器配置创建独立的采集 Scope</summary>
    IMachineScope CreateScope(MachineOptions config);
}
