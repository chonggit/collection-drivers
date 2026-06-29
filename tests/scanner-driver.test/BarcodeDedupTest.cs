using CollectionDrivers.ScannerDriver;

namespace CollectionDrivers.ScannerDriver.Test;

public class BarcodeDedupTest
{
    [Fact]
    public void IsDuplicate_SameBarcode_ReturnsTrue()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        Assert.True(dedup.IsDuplicate("ABC"));
    }

    [Fact]
    public void IsDuplicate_DifferentBarcode_ReturnsFalse()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        Assert.False(dedup.IsDuplicate("DEF"));
    }

    [Fact]
    public void Reset_ClearsLastBarcode()
    {
        var dedup = new BarcodeDedup(5000);
        Assert.False(dedup.IsDuplicate("ABC"));
        dedup.Reset();
        Assert.False(dedup.IsDuplicate("ABC"));
    }
}
