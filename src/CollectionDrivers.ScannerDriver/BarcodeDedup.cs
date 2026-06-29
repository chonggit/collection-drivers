namespace CollectionDrivers.ScannerDriver;

public class BarcodeDedup
{
    private string? _lastBarcode;
    private DateTime _lastTime;
    private readonly int _debounceMs;
    private readonly object _lock = new();

    public BarcodeDedup(int debounceMs = 2000)
    {
        _debounceMs = debounceMs;
    }

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

    public void Reset()
    {
        lock (_lock) { _lastBarcode = null; }
    }
}
