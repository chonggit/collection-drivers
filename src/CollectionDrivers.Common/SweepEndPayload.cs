namespace CollectionDrivers.Common;

/// <summary>
/// SWEEP_END 事件负载契约。TransportHandler 构建此对象，
/// Transport 实现（如 InfluxDbTransport）通过类型安全方式访问。
/// 替代匿名类型 + dynamic 的隐式契约。
/// </summary>
/// <param name="Observation">观测元数据（时间戳、机器 ID、事件名）</param>
/// <param name="Online">设备在线状态</param>
/// <param name="Healthy">设备健康状态</param>
public sealed record SweepEndPayload(
    SweepEndObservation Observation,
    bool Online,
    bool Healthy
);

/// <summary>观测元数据</summary>
public sealed record SweepEndObservation(
    long Time,
    string Machine,
    string Name
);
