using Dapr.Client;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sekiban.Pure.Dapr.Services;

// Simplified repository that uses Dapr state store for persistence
public class DaprEventStore : Repository, IEventWriter, IEventReader
{
    private readonly DaprClient _daprClient;
    private readonly DaprSekibanOptions _options;
    private readonly ILogger<DaprEventStore> _logger;

    public DaprEventStore(
        DaprClient daprClient,
        IOptions<DaprSekibanOptions> options,
        ILogger<DaprEventStore> logger)
    {
        _daprClient = daprClient;
        _options = options.Value;
        _logger = logger;
        
        // Set up serialization for the base Repository
        Serializer = @event => JsonSerializer.Serialize(@event);
        Deserializer = json => JsonSerializer.Deserialize<IEvent>(json) ?? throw new InvalidOperationException("Failed to deserialize event");
    }

    // Override Save to persist to Dapr state store
    public new async Task<ResultBox<List<IEvent>>> Save(List<IEvent> events)
    {
        var result = base.Save(events);
        if (!result.IsSuccess) return result;
        
        // Also persist to Dapr state store
        // This is a simplified implementation - in production you'd want proper event sourcing
        try
        {
            var allEvents = Events.ToList();
            await _daprClient.SaveStateAsync(
                _options.StateStoreName,
                "all-events",
                allEvents);
                
            // Publish events
            foreach (var @event in events)
            {
                await _daprClient.PublishEventAsync(
                    _options.PubSubName,
                    _options.EventTopicName,
                    @event);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            return ResultBox<List<IEvent>>.FromException(ex);
        }
    }

    // IEventWriter implementation
    public async Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        try
        {
            var eventList = events.ToList();
            
            // Save to in-memory repository
            var saveResult = await Save(eventList.Cast<IEvent>().ToList());
            if (!saveResult.IsSuccess)
            {
                throw saveResult.GetException();
            }

            // Persist to Dapr state store
            foreach (var @event in eventList)
            {
                var key = $"event:{@event.PartitionKeys.ToPrimaryKeysString()}:{@event.SortableUniqueId}";
                await _daprClient.SaveStateAsync(
                    _options.StateStoreName,
                    key,
                    @event);
            }

            // Publish events
            foreach (var @event in eventList)
            {
                await _daprClient.PublishEventAsync(
                    _options.PubSubName,
                    _options.EventTopicName,
                    @event);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving events");
            throw;
        }
    }

    // IEventReader implementation
    public async Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo retrievalInfo)
    {
        try
        {
            // For now, return from in-memory repository
            // In production, you'd query from Dapr state store
            IReadOnlyList<IEvent> events;
            
            if (retrievalInfo.GetIsPartition() && retrievalInfo.HasAggregateStream())
            {
                var aggregateId = retrievalInfo.AggregateId.GetValue();
                var streamNameResult = retrievalInfo.AggregateStream.GetValue().GetSingleStreamName();
                if (!streamNameResult.IsSuccess)
                {
                    throw streamNameResult.GetException();
                }
                var streamName = streamNameResult.GetValue();
                var rootPartitionKey = retrievalInfo.RootPartitionKey.HasValue 
                    ? retrievalInfo.RootPartitionKey.GetValue() 
                    : IDocument.DefaultRootPartitionKey;
                    
                var partitionKeys = PartitionKeys.Existing(aggregateId, streamName, rootPartitionKey);
                events = Events
                    .Where(e => e.PartitionKeys == partitionKeys)
                    .OrderBy(e => e.SortableUniqueId)
                    .ToList();
            }
            else
            {
                // Return all events if no specific partition requested
                events = Events
                    .OrderBy(e => e.SortableUniqueId)
                    .ToList();
            }
                
            return await Task.FromResult(ResultBox<IReadOnlyList<IEvent>>.FromValue(events));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving events");
            return ResultBox<IReadOnlyList<IEvent>>.FromException(ex);
        }
    }
}