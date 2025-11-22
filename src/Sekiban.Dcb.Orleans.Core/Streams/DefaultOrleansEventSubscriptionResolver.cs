using Sekiban.Dcb.Actors;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Default implementation that routes all projections to the same stream
/// </summary>
public class DefaultOrleansEventSubscriptionResolver : IEventSubscriptionResolver
{
    private readonly string _namespace;
    private readonly string _providerName;
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

    public ISekibanStream Resolve(string projectorName) =>
        // For now, all projectors use the same stream
        // In the future, this could route different projectors to different streams
        new OrleansSekibanStream(_providerName, _namespace, _streamId);
}
