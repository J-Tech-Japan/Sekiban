using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Orleans.Streams;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Publishes Sekiban Dcb events to Orleans streams using an Orleans cluster client.
/// </summary>
public class OrleansEventPublisher : IEventPublisher
{
    private readonly IClusterClient _clusterClient;
    private readonly IStreamDestinationResolver _resolver;

    public OrleansEventPublisher(IClusterClient clusterClient, IStreamDestinationResolver resolver)
    {
        _clusterClient = clusterClient;
        _resolver = resolver;
    }

    public async Task PublishAsync(IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITagCommon> Tags)> events, CancellationToken cancellationToken = default)
    {
        foreach (var (evt, tags) in events)
        {
            var streams = _resolver.Resolve(evt, tags);
            foreach (var s in streams)
            {
                if (s is OrleansSekibanStream os)
                {
                    var provider = _clusterClient.GetStreamProvider(os.ProviderName);
                    var stream = provider.GetStream<object>(StreamId.Create(os.StreamNamespace, os.StreamId));
                    // We publish the payload (or entire wrapper) depending on consumer needs; use payload by default.
                    await stream.OnNextAsync(evt.Payload);
                }
            }
        }
    }
}
