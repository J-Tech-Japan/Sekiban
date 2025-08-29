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
    private readonly ILogger<OrleansEventPublisher> _logger;
    private readonly IStreamDestinationResolver _resolver;

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

        var failedEvents = new List<(Event Event, Exception Exception)>();
        
        // Group events by stream for batch publishing
        var eventsByStream = new Dictionary<(string Provider, string Namespace, Guid StreamId), List<Event>>();
        
        foreach (var (evt, tags) in events)
        {
            try
            {
                var streams = _resolver.Resolve(evt, tags);
                foreach (var s in streams)
                {
                    if (s is OrleansSekibanStream os)
                    {
                        var key = (os.ProviderName, os.StreamNamespace, os.StreamId);
                        if (!eventsByStream.ContainsKey(key))
                        {
                            eventsByStream[key] = new List<Event>();
                        }
                        eventsByStream[key].Add(evt);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to resolve streams for event {EventType} (ID: {EventId})",
                    evt.EventType,
                    evt.Id);
                failedEvents.Add((evt, ex));
            }
        }
        
        // Publish events in batches with retry logic
        const int maxRetries = 3;
        const int retryDelayMs = 100;
        
        foreach (var ((providerName, streamNamespace, streamId), batchEvents) in eventsByStream)
        {
            _logger.LogDebug(
                "Publishing batch of {EventCount} events to stream {Provider}/{Namespace}/{StreamId}",
                batchEvents.Count,
                providerName,
                streamNamespace,
                streamId);
            
            try
            {
                var provider = _clusterClient.GetStreamProvider(providerName);
                var stream = provider.GetStream<Event>(StreamId.Create(streamNamespace, streamId));
                
                // Try to publish as batch if supported, otherwise fall back to individual
                var publishTasks = new List<Task>();
                
                foreach (var evt in batchEvents)
                {
                    // Add retry logic for each event
                    var retryCount = 0;
                    var published = false;
                    Exception? lastException = null;
                    
                    while (!published && retryCount < maxRetries)
                    {
                        try
                        {
                            await stream.OnNextAsync(evt);
                            published = true;
                            _logger.LogDebug("Event {EventId} published successfully to Orleans stream", evt.Id);
                        }
                        catch (Exception streamEx)
                        {
                            retryCount++;
                            lastException = streamEx;
                            
                            if (retryCount < maxRetries)
                            {
                                _logger.LogWarning(
                                    "Failed to publish event {EventId} on attempt {Attempt}, retrying...",
                                    evt.Id,
                                    retryCount);
                                await Task.Delay(retryDelayMs * retryCount, cancellationToken);
                            }
                        }
                    }
                    
                    if (!published && lastException != null)
                    {
                        _logger.LogError(
                            lastException,
                            "Failed to publish event {EventType} (ID: {EventId}) after {MaxRetries} retries",
                            evt.EventType,
                            evt.Id,
                            maxRetries);
                        failedEvents.Add((evt, lastException));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get stream provider or stream for {Provider}/{Namespace}/{StreamId}",
                    providerName,
                    streamNamespace,
                    streamId);
                    
                foreach (var evt in batchEvents)
                {
                    failedEvents.Add((evt, ex));
                }
            }
        }

        if (failedEvents.Any())
        {
            _logger.LogWarning(
                "Failed to publish {FailedCount} out of {TotalCount} events to Orleans streams. These events are persisted in the event store and will be available through catch-up reads.",
                failedEvents.Count,
                events.Count);

            // Log details of failed events for debugging
            foreach (var (evt, ex) in failedEvents)
            {
                _logger.LogDebug(
                    "Failed event details - Type: {EventType}, ID: {EventId}, Error: {ErrorMessage}",
                    evt.EventType,
                    evt.Id,
                    ex.Message);
            }
        } else if (events.Any())
        {
            _logger.LogInformation("Successfully published all {EventCount} events to Orleans streams", events.Count);
        }
    }
}
