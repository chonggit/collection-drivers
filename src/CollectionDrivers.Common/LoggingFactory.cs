using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading;

// ReSharper disable once CheckNamespace
namespace CollectionDrivers.Common;

[Obsolete("Phase 5 将删除。DI 化后用构造函数注入 ILogger<T> 替代。")]
public static class LoggingFactory
{
    private static ILoggerFactory _factory = NullLoggerFactory.Instance;
    private static readonly object _lock = new();

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

    public static ILogger CreateLogger(string categoryName)
        => Volatile.Read(ref _factory).CreateLogger(categoryName);

}
