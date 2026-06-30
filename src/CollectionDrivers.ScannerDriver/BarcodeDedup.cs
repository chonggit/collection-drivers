namespace CollectionDrivers.ScannerDriver;

/// <summary>
/// 条码去重器。在去抖时间窗口内重复扫描同一条码时返回 true，防止重复上报。
/// </summary>
public class BarcodeDedup
{
    private string? _lastBarcode;
    private DateTime _lastTime;
    private readonly int _debounceMs;
    private readonly object _lock = new();

    /// <summary>构造去重器</summary>
    /// <param name="debounceMs">去抖时间窗口（毫秒），默认 2000</param>
    public BarcodeDedup(int debounceMs = 2000)
    {
        _debounceMs = debounceMs;
    }

    /// <summary>检查是否为重复条码。在去抖窗口内相同条码返回 true。</summary>
    public bool IsDuplicate(string barcode)
    {
        lock (_lock)
        {
            if (barcode == _lastBarcode &&
                (DateTime.UtcNow - _lastTime).TotalMilliseconds < _debounceMs)
                return true;

            _lastBarcode = barcode;
            _lastTime = DateTime.UtcNow;
            return false;
        }
    }

    /// <summary>重置去重状态</summary>
    public void Reset()
    {
        lock (_lock) { _lastBarcode = null; }
    }
}
