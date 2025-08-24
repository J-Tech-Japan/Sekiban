using Sekiban.Dcb.Actors;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Routes different projectors to different streams based on configuration
/// </summary>
public class ConfigurableOrleansEventSubscriptionResolver : IEventSubscriptionResolver
{
    private readonly string _defaultNamespace;
    private readonly string _defaultProviderName;
    private readonly Guid _defaultStreamId;
    private readonly Dictionary<string, (string ProviderName, string Namespace, Guid StreamId)> _projectorConfigs;

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

    public ISekibanStream Resolve(string projectorName)
    {
        if (_projectorConfigs.TryGetValue(projectorName, out var config))
        {
            return new OrleansSekibanStream(config.ProviderName, config.Namespace, config.StreamId);
        }

        // Fall back to default configuration
        return new OrleansSekibanStream(_defaultProviderName, _defaultNamespace, _defaultStreamId);
    }
}
