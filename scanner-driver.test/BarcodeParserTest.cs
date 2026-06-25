using scanner.driver;
using scanner.driver.models;

namespace scanner.driver.test;

public class BarcodeParserTest
{
    [Fact]
    public void Parse_Ascii_ReturnsTrimmed()
    {
        var proto = new ProtocolConfig { ResponseEncoding = "ascii" };
        var parser = new BarcodeParser(proto);
        var result = parser.Parse("ABC123"u8.ToArray());
        Assert.Equal("ABC123", result);
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        var parser = new BarcodeParser(new ProtocolConfig());
        Assert.Null(parser.Parse(null));
    }

    [Fact]
    public void Parse_Regex_ExtractsGroup()
    {
        var proto = new ProtocolConfig
        {
            BarcodeRegex = "<p>(.*?)</p>",
            RegexGroupIndex = 1
        };
        var parser = new BarcodeParser(proto);
        var data = "<p>BARCODE123</p>"u8.ToArray();
        var result = parser.Parse(data);
        Assert.Equal("BARCODE123", result);
    }

    [Fact]
    public void Parse_RemovePrefix_StripsStart()
    {
        var proto = new ProtocolConfig
        {
            RemovePrefixes = new[] { "STX" },
            RemoveSuffixes = new[] { "ETX" }
        };
        var parser = new BarcodeParser(proto);
        var result = parser.Parse("STXABC123ETX"u8.ToArray());
        Assert.Equal("ABC123", result);
    }
}
