using Microsoft.Extensions.Logging;
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
    private readonly ILogger<OrleansEventPublisher> _logger;

    public OrleansEventPublisher(
        IClusterClient clusterClient, 
        IStreamDestinationResolver resolver,
        ILogger<OrleansEventPublisher> logger)
    {
        _clusterClient = clusterClient;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Publishing {EventCount} events to Orleans streams", events.Count);
        Console.WriteLine($"[OrleansEventPublisher] Publishing {events.Count} events");
        
        var failedEvents = new List<(Event Event, Exception Exception)>();
        
        foreach (var (evt, tags) in events)
        {
            try
            {
                var streams = _resolver.Resolve(evt, tags);
                foreach (var s in streams)
                {
                    if (s is OrleansSekibanStream os)
                    {
                        _logger.LogDebug(
                            "Publishing event {EventType} (ID: {EventId}, SortableUniqueId: {SortableUniqueId}) to stream {Provider}/{Namespace}/{StreamId}",
                            evt.EventType, evt.Id, evt.SortableUniqueIdValue, os.ProviderName, os.StreamNamespace, os.StreamId);
                        Console.WriteLine($"[OrleansEventPublisher] Publishing event {evt.EventType} to stream {os.ProviderName}/{os.StreamNamespace}/{os.StreamId}");
                        Console.WriteLine($"[OrleansEventPublisher] Event ID: {evt.Id}, SortableUniqueId: {evt.SortableUniqueIdValue}");
                        
                        try
                        {
                            var provider = _clusterClient.GetStreamProvider(os.ProviderName);
                            var stream = provider.GetStream<Event>(StreamId.Create(os.StreamNamespace, os.StreamId));
                            
                            // Publish the entire Event object
                            await stream.OnNextAsync(evt);
                            
                            _logger.LogDebug("Event {EventId} published successfully to Orleans stream", evt.Id);
                            Console.WriteLine($"[OrleansEventPublisher] Event {evt.Id} published successfully to Orleans stream");
                        }
                        catch (Exception streamEx)
                        {
                            _logger.LogError(streamEx, 
                                "Failed to publish event {EventType} (ID: {EventId}) to Orleans stream {Provider}/{Namespace}/{StreamId}",
                                evt.EventType, evt.Id, os.ProviderName, os.StreamNamespace, os.StreamId);
                            Console.WriteLine($"[OrleansEventPublisher] ERROR: Failed to publish event {evt.Id}: {streamEx.Message}");
                            failedEvents.Add((evt, streamEx));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to resolve streams for event {EventType} (ID: {EventId})",
                    evt.EventType, evt.Id);
                Console.WriteLine($"[OrleansEventPublisher] ERROR: Failed to resolve streams for event {evt.Id}: {ex.Message}");
                failedEvents.Add((evt, ex));
            }
        }
        
        if (failedEvents.Any())
        {
            _logger.LogWarning(
                "Failed to publish {FailedCount} out of {TotalCount} events to Orleans streams. These events are persisted in the event store and will be available through catch-up reads.",
                failedEvents.Count, events.Count);
            Console.WriteLine($"[OrleansEventPublisher] WARNING: {failedEvents.Count} events failed to publish to streams but are saved in event store");
            
            // Log details of failed events for debugging
            foreach (var (evt, ex) in failedEvents)
            {
                _logger.LogDebug(
                    "Failed event details - Type: {EventType}, ID: {EventId}, Error: {ErrorMessage}",
                    evt.EventType, evt.Id, ex.Message);
            }
        }
        else if (events.Any())
        {
            _logger.LogInformation("Successfully published all {EventCount} events to Orleans streams", events.Count);
        }
    }
}
