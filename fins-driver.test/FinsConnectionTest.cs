using fins.driver;

namespace fins.driver.test;

public class FinsConnectionTest
{
    [Fact]
    public void Constructor_SetsDefaults()
    {
        var conn = new FinsConnection("192.168.250.1");
        Assert.False(conn.IsConnected);
    }

    [Fact]
    public void Connect_InvalidIp_Throws()
    {
        var conn = new FinsConnection("not-an-ip");
        Assert.Throws<FormatException>(() => conn.Connect());
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var conn = new FinsConnection("192.168.250.1", 9600, 2000);
        conn.Dispose();
        conn.Dispose();
    }

    [Fact]
    public async Task ReadDAsync_NotConnected_Throws()
    {
        var conn = new FinsConnection("192.168.250.1");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conn.ReadDAsync(100, 10));
    }
}
