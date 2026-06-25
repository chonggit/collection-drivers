using System.IO;
using l99.driver.@base;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using opcua.driver.models;

namespace opcua.driver.strategies;

public class OpcUaStrategy : Strategy, IAsyncDisposable
{
    private readonly OpcUaConfig _config;
    private ApplicationConfiguration? _appConfig;
    private Session? _session;
    private SessionReconnectHandler? _reconnectHandler;
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly Dictionary<string, DateTime> _lastSweepTime = new();
    private CancellationTokenSource? _disposeCts;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public event Action<string, Dictionary<string, object>>? OnData;
    public event Action<Exception, string>? OnError;
    public event Action<bool, string>? OnConnectionState;

    public OpcUaStrategy(Machine machine) : base(machine)
    {
        var rawConfig = machine.Configuration.strategy;
        _config = ParseConfig(rawConfig);
    }

    public static OpcUaConfig ParseConfig(dynamic rawConfig)
    {
        var config = new OpcUaConfig();
        if (rawConfig == null) return config;

        try
        {
            var dict = rawConfig as IDictionary<string, object>;

            if (dict != null)
            {
                if (dict.ContainsKey("endpoint"))
                    config.Endpoint = (string)dict["endpoint"];
                if (dict.ContainsKey("use_security"))
                    config.UseSecurity = (bool)dict["use_security"];
                if (dict.ContainsKey("reconnect_period_ms"))
                    config.ReconnectPeriodMs = Convert.ToInt32(dict["reconnect_period_ms"]);
                if (dict.ContainsKey("auto_accept_certs"))
                    config.AutoAcceptCerts = (bool)dict["auto_accept_certs"];
                if (dict.ContainsKey("user_name"))
                    config.UserName = (string?)dict["user_name"];
                if (dict.ContainsKey("password"))
                    config.Password = (string?)dict["password"];

                if (dict.ContainsKey("collectors") && dict["collectors"] is System.Collections.IList collectors)
                {
                    var collectorList = new List<CollectorConfig>();
                    foreach (var cObj in collectors)
                    {
                        var c = cObj as IDictionary<string, object>;
                        if (c == null) continue;

                        var cc = new CollectorConfig
                        {
                            Name = (string)c["name"],
                            Mode = (string)c["mode"],
                            SamplingIntervalMs = c.ContainsKey("sampling_interval_ms")
                                ? Convert.ToInt32(c["sampling_interval_ms"]) : 100
                        };
                        if (c.ContainsKey("sweep_interval_ms"))
                            cc.SweepIntervalMs = Convert.ToInt32(c["sweep_interval_ms"]);
                        if (c.ContainsKey("nodes") && c["nodes"] is System.Collections.IList nodes)
                        {
                            var nodeList = new List<NodeConfig>();
                            foreach (var nObj in nodes)
                            {
                                var n = nObj as IDictionary<string, object>;
                                if (n == null) continue;
                                nodeList.Add(new NodeConfig
                                {
                                    Id = (string)n["id"],
                                    Alias = n.ContainsKey("alias") ? (string?)n["alias"] : null
                                });
                            }
                            cc.Nodes = nodeList.ToArray();
                        }
                        collectorList.Add(cc);
                    }
                    config.Collectors = collectorList.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse OPC UA strategy configuration", ex);
        }

        return config;
    }

    // ================ InitializeAsync ================

    public override async Task<dynamic?> InitializeAsync()
    {
        _disposeCts = new CancellationTokenSource();
        _appConfig = CreateApplicationConfig();

        try
        {
            // Validate collectors
            foreach (var collector in _config.Collectors)
            {
                if (collector.Mode != "subscription" && collector.Mode != "poll")
                    OnError?.Invoke(
                        new InvalidDataException(
                            $"Unknown collector mode '{collector.Mode}' for '{collector.Name}'"),
                        "InitializeAsync");
            }

            var duplicateNames = _config.Collectors
                .GroupBy(c => c.Name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var name in duplicateNames)
                OnError?.Invoke(
                    new InvalidDataException($"Duplicate collector name '{name}'"),
                    "InitializeAsync");

            // Select endpoint via discovery
            var discoveryClient = DiscoveryClient.Create(
                new Uri(_config.Endpoint));
            var endpointDescriptions = discoveryClient.GetEndpoints(
                new StringCollection { _config.Endpoint });
            discoveryClient.Close();
            var selectedEndpoint = _config.UseSecurity
                ? endpointDescriptions.FirstOrDefault(e => e.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                : endpointDescriptions.FirstOrDefault(e => e.SecurityMode == MessageSecurityMode.None);
            var endpointDescription = selectedEndpoint ?? endpointDescriptions.FirstOrDefault();
            if (endpointDescription == null)
                throw new Exception($"No OPC UA endpoint found at {_config.Endpoint}");

            var endpointConfiguration = EndpointConfiguration.Create(_appConfig);
            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription, endpointConfiguration);

            // Build user identity
            IUserIdentity userIdentity;
            if (!string.IsNullOrEmpty(_config.UserName))
                userIdentity = new UserIdentity(
                    _config.UserName,
                    System.Text.Encoding.UTF8.GetBytes(_config.Password ?? ""));
            else
                userIdentity = new UserIdentity(new AnonymousIdentityToken());

            // Create session
            _session = await Session.Create(
                _appConfig,
                configuredEndpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "OpcUaDriver",
                sessionTimeout: 60000u,
                identity: userIdentity,
                preferredLocales: null);

            _session.KeepAlive += (sender, e) =>
            {
                if (!ServiceResult.IsGood(e.Status))
                {
                    OnError?.Invoke(
                        new Exception($"KeepAlive failed: {e.Status}"), "KeepAlive");
                    OnConnectionState?.Invoke(false, "Disconnected");
                    _ = ReconnectAndRestoreAsync();
                }
            };

            OnConnectionState?.Invoke(true, "Connected");

            // Create subscriptions
            foreach (var collector in _config.Collectors.Where(c =>
                c.Mode == "subscription" && c.Nodes.Length > 0))
            {
                CreateSubscription(collector);
            }
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "InitializeAsync");
            throw;
        }

        return null;
    }

    private ApplicationConfiguration CreateApplicationConfig()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = "OpcUaDriver",
            ApplicationType = ApplicationType.Client,
            CertificateValidator = new CertificateValidator
            {
                AutoAcceptUntrustedCertificates = _config.AutoAcceptCerts
            },
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = _config.AutoAcceptCerts,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 30000,
                MaxStringLength = int.MaxValue,
                MaxArrayLength = 65535,
                MaxMessageSize = 419430400,
                MaxBufferSize = 65535
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000,
                MinSubscriptionLifetime = 60000
            }
        };

        config.CertificateValidator.CertificateValidation += (_, eventArgs) =>
        {
            if (ServiceResult.IsGood(eventArgs.Error))
                eventArgs.Accept = true;
            else if (eventArgs.Error.StatusCode.Code == 0x80000000u)
                eventArgs.Accept = _config.AutoAcceptCerts;
        };

        return config;
    }

    private void CreateSubscription(CollectorConfig collector)
    {
        if (_session == null) return;

        var sub = new Subscription(_session.DefaultSubscription)
        {
            PublishingInterval = collector.SamplingIntervalMs,
            DisplayName = collector.Name,
            PublishingEnabled = true
        };

        foreach (var node in collector.Nodes)
        {
            NodeId nodeId;
            try
            {
                nodeId = new NodeId(node.Id);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, $"Invalid nodeId: {node.Id}");
                continue;
            }

            var item = new MonitoredItem
            {
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                DisplayName = node.Alias ?? node.Id,
                SamplingInterval = collector.SamplingIntervalMs,
                QueueSize = 1,
                DiscardOldest = true
            };

            item.Notification += (MonitoredItem monitoredItem,
                MonitoredItemNotificationEventArgs args) =>
            {
                if (args.NotificationValue is MonitoredItemNotification notification)
                    ProcessNotification(collector, monitoredItem, notification);
            };
            sub.AddItem(item);
        }

        if (sub.MonitoredItemCount > 0)
        {
            try
            {
                _session.AddSubscription(sub);
                sub.Create();
                _subscriptions[collector.Name] = sub;
            }
            catch (Exception ex)
            {
                try { _session.RemoveSubscription(sub); } catch { }
                sub.Dispose();
                OnError?.Invoke(ex, $"CreateSubscription.{collector.Name}");
            }
        }
        else
        {
            sub.Dispose();
        }
    }

    private void ProcessNotification(CollectorConfig collector,
        MonitoredItem monitoredItem, MonitoredItemNotification notification)
    {
        if (notification.Value != null &&
            ServiceResult.IsBad(notification.Value.StatusCode))
        {
            OnError?.Invoke(
                new InvalidDataException(
                    $"Bad quality: {notification.Value.StatusCode} for {monitoredItem.DisplayName}"),
                $"Subscription.{collector.Name}");
            return;
        }

        var rawValue = notification.Value?.Value;
        if (rawValue == null)
        {
            OnError?.Invoke(
                new InvalidDataException(
                    $"Null value for node {monitoredItem.DisplayName}"),
                $"Subscription.{collector.Name}");
            return;
        }

        var dict = new Dictionary<string, object>
        {
            [monitoredItem.DisplayName] = rawValue
        };
        OnData?.Invoke(collector.Name, dict);
    }

    // ================ SweepAsync ================

    public override async Task SweepAsync(int delayMs = -1)
    {
        await Task.Delay(delayMs < 0 ? SweepMs : delayMs);

        if (_session == null || !_session.Connected)
        {
            await Machine.Handler.OnStrategySweepCompleteInternalAsync();
            return;
        }

        foreach (var collector in _config.Collectors.Where(c =>
            c.Mode == "poll" && c.Nodes.Length > 0))
        {
            var lastSweep = _lastSweepTime.GetValueOrDefault(
                collector.Name, DateTime.MinValue);
            var interval = collector.SweepIntervalMs ?? 5000;
            if ((DateTime.UtcNow - lastSweep).TotalMilliseconds < interval)
                continue;

            _lastSweepTime[collector.Name] = DateTime.UtcNow;

            try
            {
                var validNodes = new List<(NodeConfig config, NodeId nodeId)>();
                foreach (var n in collector.Nodes)
                {
                    try
                    {
                        validNodes.Add((n, new NodeId(n.Id)));
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(ex,
                            $"Invalid nodeId: {n.Id} in collector {collector.Name}");
                    }
                }

                if (validNodes.Count == 0) continue;

                var nodesToRead = new ReadValueIdCollection(
                    validNodes.Select(v => new ReadValueId
                    {
                        NodeId = v.nodeId,
                        AttributeId = Attributes.Value
                    }));

                using var readCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));
                var response = await _session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    readCts.Token);
                var results = response.Results as DataValueCollection;
                if (results == null) continue;

                var dict = new Dictionary<string, object>();
                for (int i = 0; i < validNodes.Count && i < results.Count; i++)
                {
                    var key = validNodes[i].config.Alias ?? validNodes[i].config.Id;
                    var dataValue = results[i];

                    if (dataValue != null &&
                        ServiceResult.IsBad(dataValue.StatusCode))
                    {
                        OnError?.Invoke(
                            new InvalidDataException(
                                $"Bad quality: {dataValue.StatusCode} for {key}"),
                            $"Sweep.collector={collector.Name}");
                        continue;
                    }

                    var rawValue = dataValue?.Value;
                    if (rawValue != null)
                        dict[key] = rawValue;
                    else
                        OnError?.Invoke(
                            new InvalidDataException($"Null value for node {key}"),
                            $"Sweep.collector={collector.Name}");
                }

                if (dict.Count > 0)
                    OnData?.Invoke(collector.Name, dict);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex, $"Sweep collector={collector.Name}");
            }
        }

        await Machine.Handler.OnStrategySweepCompleteInternalAsync();
    }

    // ================ Reconnect ================

    private async Task ReconnectAndRestoreAsync()
    {
        if (!await _reconnectLock.WaitAsync(0))
            return;

        try
        {
            if (_disposeCts?.IsCancellationRequested == true)
                return;

            _reconnectHandler?.Dispose();
            _reconnectHandler = new SessionReconnectHandler();

            var tcs = new TaskCompletionSource<bool>();
            _reconnectHandler.BeginReconnect(
                _session!,
                _config.ReconnectPeriodMs,
                (sender, e) =>
                {
                    tcs.TrySetResult(true);
                });

            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, _disposeCts!.Token);

            if (await Task.WhenAny(tcs.Task,
                    Task.Delay(-1, linkedCts.Token)) != tcs.Task)
            {
                OnError?.Invoke(
                    new TimeoutException("Reconnect timeout"), "ReconnectAsync");
                return;
            }

            // Clean old subscriptions
            foreach (var kvp in _subscriptions)
            {
                try
                {
                    _session?.RemoveSubscription(kvp.Value);
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _subscriptions.Clear();

            // Rebuild subscriptions
            foreach (var collector in _config.Collectors.Where(
                c => c.Mode == "subscription"))
                CreateSubscription(collector);

            OnConnectionState?.Invoke(true, "Reconnected");
        }
        catch (Exception ex)
        {
            OnError?.Invoke(ex, "ReconnectAsync");
        }
        finally
        {
            _reconnectLock.Release();
        }
    }

    // ================ Dispose ================

    public async ValueTask DisposeAsync()
    {
        _disposeCts?.Cancel();
        await _reconnectLock.WaitAsync();
        try
        {
            foreach (var kvp in _subscriptions)
            {
                try
                {
                    _session?.RemoveSubscription(kvp.Value);
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _subscriptions.Clear();

            _reconnectHandler?.Dispose();
            _reconnectHandler = null;

            if (_session != null)
            {
                try { await _session.CloseAsync(); } catch { }
                _session.Dispose();
                _session = null;
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
        _disposeCts?.Dispose();
        OnConnectionState?.Invoke(false, "Disposed");
    }

    public bool IsConnected => _session?.Connected ?? false;
}
