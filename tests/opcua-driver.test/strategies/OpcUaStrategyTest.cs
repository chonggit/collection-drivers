using System.Dynamic;
using opcua.driver.strategies;
using opcua.driver.models;

namespace opcua.driver.test.strategies;

public class OpcUaStrategyTest
{
    [Fact]
    public void ParseConfig_ReadsAllFields()
    {
        // Use IDictionary<string, object> for ExpandoObject property access
        dynamic configObj = new ExpandoObject();
        var config = (IDictionary<string, object>)configObj;
        config["endpoint"] = "opc.tcp://localhost:4840";
        config["use_security"] = true;
        config["reconnect_period_ms"] = 5000;
        config["auto_accept_certs"] = false;
        config["user_name"] = "admin";
        config["password"] = "pass123";

        dynamic c1Obj = new ExpandoObject();
        var c1 = (IDictionary<string, object>)c1Obj;
        c1["name"] = "sub1";
        c1["mode"] = "subscription";
        c1["sampling_interval_ms"] = 200;

        dynamic n1Obj = new ExpandoObject();
        var n1 = (IDictionary<string, object>)n1Obj;
        n1["id"] = "ns=2;s=Test.A";
        n1["alias"] = "test_a";
        c1["nodes"] = new List<dynamic> { n1Obj };

        dynamic c2Obj = new ExpandoObject();
        var c2 = (IDictionary<string, object>)c2Obj;
        c2["name"] = "poll1";
        c2["mode"] = "poll";
        c2["sweep_interval_ms"] = 3000;
        c2["nodes"] = new List<dynamic>();

        config["collectors"] = new List<dynamic> { c1Obj, c2Obj };

        var result = OpcUaStrategy.ParseConfig(configObj);

        Assert.Equal("opc.tcp://localhost:4840", result.Endpoint);
        Assert.True(result.UseSecurity);
        Assert.Equal(5000, result.ReconnectPeriodMs);
        Assert.False(result.AutoAcceptCerts);
        Assert.Equal("admin", result.UserName);
        Assert.Equal(2, result.Collectors.Length);
        Assert.Equal("sub1", result.Collectors[0].Name);
        Assert.Equal("subscription", result.Collectors[0].Mode);
        Assert.Equal(200, result.Collectors[0].SamplingIntervalMs);
        Assert.Equal("ns=2;s=Test.A", result.Collectors[0].Nodes[0].Id);
        Assert.Equal("test_a", result.Collectors[0].Nodes[0].Alias);
        Assert.Equal("poll", result.Collectors[1].Mode);
        Assert.Equal(3000, result.Collectors[1].SweepIntervalMs);
    }

    [Fact]
    public void ParseConfig_NullInput_ReturnsDefaults()
    {
        var result = OpcUaStrategy.ParseConfig(null!);
        Assert.NotNull(result);
        Assert.Equal("", result.Endpoint);
    }

    [Fact]
    public void ParseConfig_EmptyCollectors_ReturnsEmptyArray()
    {
        dynamic configObj = new ExpandoObject();
        var config = (IDictionary<string, object>)configObj;
        config["endpoint"] = "opc.tcp://localhost:4840";
        config["collectors"] = new List<dynamic>();

        var result = OpcUaStrategy.ParseConfig(configObj);
        Assert.Empty(result.Collectors);
    }
}
