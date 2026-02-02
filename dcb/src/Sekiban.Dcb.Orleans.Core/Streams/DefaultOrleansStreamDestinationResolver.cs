using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Default resolver that routes all events to Orleans provider "EventStreamProvider",
///     namespace "AllEvents" and id Guid.Empty, mirroring AggregateEventHandlerGrain.
/// </summary>
public class DefaultOrleansStreamDestinationResolver : IStreamDestinationResolver
{
    private readonly string _namespace;
    private readonly string _providerName;
    private readonly Guid _streamId;
    private readonly IServiceIdProvider _serviceIdProvider;

    public DefaultOrleansStreamDestinationResolver(
        string providerName = "EventStreamProvider",
        string @namespace = "AllEvents",
        Guid? streamId = null,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _providerName = providerName;
        _namespace = @namespace;
        _streamId = streamId ?? Guid.Empty;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace(_namespace, serviceId);
        yield return new OrleansSekibanStream(_providerName, streamNamespace, _streamId);
    }
}
