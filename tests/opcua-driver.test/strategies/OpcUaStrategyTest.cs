using System.Dynamic;
using CollectionDrivers.OpcUaDriver.Strategies;
using CollectionDrivers.OpcUaDriver.Models;

namespace CollectionDrivers.OpcUaDriver.Test.Strategies;

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

    /// <summary>
    /// Bug F9 复现：YamlDotNet 默认反序列化产生 Dictionary&lt;object,object&gt;，
    /// 而 OpcUaStrategy.ParseConfig 仅处理 IDictionary&lt;string,object&gt;，
    /// 导致配置被静默丢弃（返回默认空配置）。
    /// FinsStrategy 和 ScannerStrategy 已有回退逻辑。
    /// </summary>
    [Fact]
    public void ParseConfig_DictionaryObjectObject_ReadsConfig()
    {
        // 模拟 YamlDotNet 反序列化产物：Dictionary<object, object>
        var collectorNodes = new List<object>
        {
            new Dictionary<object, object>
            {
                ["id"] = "ns=2;s=Test.Value",
                ["alias"] = "test_value"
            }
        };

        var collectors = new List<object>
        {
            new Dictionary<object, object>
            {
                ["name"] = "sub1",
                ["mode"] = "subscription",
                ["sampling_interval_ms"] = 500,
                ["nodes"] = collectorNodes
            }
        };

        var rawConfig = new Dictionary<object, object>
        {
            ["endpoint"] = "opc.tcp://192.168.1.1:4840",
            ["use_security"] = false,
            ["reconnect_period_ms"] = 10000,
            ["auto_accept_certs"] = true,
            ["collectors"] = collectors
        };

        var result = OpcUaStrategy.ParseConfig(rawConfig);

        // RED: 当前代码返回空配置（Endpoint=""），因为 as IDictionary<string,object> 返回 null
        // GREEN: 修复后正确解析所有字段
        Assert.Equal("opc.tcp://192.168.1.1:4840", result.Endpoint);
        Assert.False(result.UseSecurity);
        Assert.Equal(10000, result.ReconnectPeriodMs);
        Assert.True(result.AutoAcceptCerts);
        Assert.Single(result.Collectors);
        Assert.Equal("sub1", result.Collectors[0].Name);
        Assert.Equal("subscription", result.Collectors[0].Mode);
        Assert.Equal(500, result.Collectors[0].SamplingIntervalMs);
        Assert.Single(result.Collectors[0].Nodes);
        Assert.Equal("ns=2;s=Test.Value", result.Collectors[0].Nodes[0].Id);
    }
}
