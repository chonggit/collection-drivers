using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace l99.driver.@base;

/// <summary>
/// 静态日志工厂，封装 ILoggerFactory 的生命周期。
/// 未配置时默认使用 NullLogger（无日志输出）。
/// 宿主在启动时调用 SetProvider() 配置具体日志提供程序。
/// </summary>
public static class LoggingFactory
{
    private static ILoggerFactory _factory = NullLoggerFactory.Instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 设置日志提供程序。由宿主在应用程序启动时调用。
    /// 未调用时默认使用 NullLogger（无日志输出）。
    /// </summary>
    /// <param name="factory">日志工厂实例。不允许为 null。</param>
    /// <exception cref="ArgumentNullException">factory 为 null 时抛出</exception>
    public static void SetProvider(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_lock)
        {
            var old = _factory;
            _factory = factory;
            (old as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 获取或创建指定类别名称的日志记录器。
    /// 使用 Volatile.Read 确保多线程下的引用可见性。
    /// </summary>
    public static ILogger CreateLogger(string categoryName)
        => Volatile.Read(ref _factory).CreateLogger(categoryName);

    /// <summary>
    /// 释放底层日志工厂，并将工厂重置为 NullLogger。
    /// 宿主应在应用程序关闭时调用。重置后日志静默丢弃。
    /// </summary>
    public static void Close()
    {
        lock (_lock)
        {
            (_factory as IDisposable)?.Dispose();
            _factory = NullLoggerFactory.Instance;
        }
    }
}
