using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Default implementation that routes all projections to the same stream
/// </summary>
public class DefaultOrleansEventSubscriptionResolver : IEventSubscriptionResolver
{
    private readonly string _providerName;
    private readonly string _namespace;
    private readonly Guid _streamId;

    public DefaultOrleansEventSubscriptionResolver(
        string providerName = "EventStreamProvider",
        string @namespace = "AllEvents",
        Guid? streamId = null)
    {
        _providerName = providerName;
        _namespace = @namespace;
        _streamId = streamId ?? Guid.Empty;
    }

    public IEventSubscription Resolve(
        string projectorName,
        Func<string, string, Guid, IEventSubscription> subscriptionFactory)
    {
        // For now, all projectors use the same stream
        // In the future, this could route different projectors to different streams
        return subscriptionFactory(_providerName, _namespace, _streamId);
    }
}

/// <summary>
/// Routes different projectors to different streams based on configuration
/// </summary>
public class ConfigurableOrleansEventSubscriptionResolver : IEventSubscriptionResolver
{
    private readonly Dictionary<string, (string ProviderName, string Namespace, Guid StreamId)> _projectorConfigs;
    private readonly string _defaultProviderName;
    private readonly string _defaultNamespace;
    private readonly Guid _defaultStreamId;

    public ConfigurableOrleansEventSubscriptionResolver(
        Dictionary<string, (string ProviderName, string Namespace, Guid StreamId)>? projectorConfigs = null,
        string defaultProviderName = "EventStreamProvider",
        string defaultNamespace = "AllEvents",
        Guid? defaultStreamId = null)
    {
        _projectorConfigs = projectorConfigs ?? new Dictionary<string, (string, string, Guid)>();
        _defaultProviderName = defaultProviderName;
        _defaultNamespace = defaultNamespace;
        _defaultStreamId = defaultStreamId ?? Guid.Empty;
    }

    public IEventSubscription Resolve(
        string projectorName,
        Func<string, string, Guid, IEventSubscription> subscriptionFactory)
    {
        if (_projectorConfigs.TryGetValue(projectorName, out var config))
        {
            return subscriptionFactory(config.ProviderName, config.Namespace, config.StreamId);
        }

        // Fall back to default configuration
        return subscriptionFactory(_defaultProviderName, _defaultNamespace, _defaultStreamId);
    }
}