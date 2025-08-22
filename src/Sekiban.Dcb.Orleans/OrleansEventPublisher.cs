using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Publishes Sekiban Dcb events to Orleans streams using an Orleans cluster client.
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

    public async Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[OrleansEventPublisher] Publishing {events.Count} events");
        
        foreach (var (evt, tags) in events)
        {
            var streams = _resolver.Resolve(evt, tags);
            foreach (var s in streams)
            {
                if (s is OrleansSekibanStream os)
                {
                    Console.WriteLine($"[OrleansEventPublisher] Publishing event {evt.EventType} to stream {os.ProviderName}/{os.StreamNamespace}/{os.StreamId}");
                    
                    var provider = _clusterClient.GetStreamProvider(os.ProviderName);
                    var stream = provider.GetStream<Event>(StreamId.Create(os.StreamNamespace, os.StreamId));
                    // Publish the entire Event object, not just the payload
                    await stream.OnNextAsync(evt);
                    
                    Console.WriteLine($"[OrleansEventPublisher] Event published successfully");
                }
            }
        }
    }
}
