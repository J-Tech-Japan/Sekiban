using Dapr.Actors;
using Dapr.Actors.Runtime;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using ResultBoxes;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor implementation for handling event persistence and retrieval for aggregate streams.
/// This is the Dapr equivalent of Orleans' AggregateEventHandlerGrain.
/// </summary>
public class AggregateEventHandlerActor : Actor, IAggregateEventHandlerActor
{
    private readonly IEventWriter _eventWriter;
    private readonly IEventReader _eventReader;
    private readonly SekibanDomainTypes _sekibanDomainTypes;
    private readonly ILogger<AggregateEventHandlerActor> _logger;
    
    // State key for persisting the last event information
    private const string StateKey = "aggregateEventHandler";
    
    public AggregateEventHandlerActor(
        ActorHost host,
        IEventWriter eventWriter,
        IEventReader eventReader,
        SekibanDomainTypes sekibanDomainTypes,
        ILogger<AggregateEventHandlerActor> logger) : base(host)
    {
        _eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));
        _eventReader = eventReader ?? throw new ArgumentNullException(nameof(eventReader));
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Legacy method - kept for compatibility
    public async Task<IReadOnlyList<IEvent>> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        IReadOnlyList<IEvent> newEvents)
    {
        if (newEvents == null || newEvents.Count == 0)
        {
            return Array.Empty<IEvent>();
        }

        // Get current state
        var persistedState = await StateManager.TryGetStateAsync<AggregateEventHandlerState>(StateKey);
        var currentState = persistedState.HasValue 
            ? persistedState.Value 
            : new AggregateEventHandlerState { LastSortableUniqueId = string.Empty };

        // Optimistic concurrency check
        if (!string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) && 
            currentState.LastSortableUniqueId != expectedLastSortableUniqueId)
        {
            throw new InvalidOperationException(
                $"Expected last event ID '{expectedLastSortableUniqueId}' does not match actual '{currentState.LastSortableUniqueId}'");
        }

        // Process events with timestamps
        var toStoreEvents = newEvents.ToList().ToEventsAndReplaceTime(_sekibanDomainTypes.EventTypes.GetEventTypes());
        
        // Validate event ordering
        if (!string.IsNullOrWhiteSpace(currentState.LastSortableUniqueId) && 
            toStoreEvents.Any() &&
            string.Compare(
                currentState.LastSortableUniqueId,
                toStoreEvents.First().SortableUniqueId,
                StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException(
                "New events must have sortable unique IDs later than the current last event");
        }

        // Save events to persistent storage
        if (toStoreEvents.Count > 0)
        {
            await _eventWriter.SaveEvents(toStoreEvents);
            
            // Update state with last event information
            var lastEvent = toStoreEvents.Last();
            var newState = new AggregateEventHandlerState
            {
                LastSortableUniqueId = lastEvent.SortableUniqueId,
                LastEventDate = new SortableUniqueIdValue(lastEvent.SortableUniqueId).GetTicks()
            };
            
            await StateManager.SetStateAsync(StateKey, newState);
        }

        return toStoreEvents;
    }

    // Legacy method - kept for compatibility
    public async Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        var retrievalInfo = GetEventRetrievalInfo();
        var allEvents = await _eventReader.GetEvents(retrievalInfo).UnwrapBox();
        
        if (string.IsNullOrWhiteSpace(fromSortableUniqueId))
        {
            return limit > 0 
                ? allEvents.Take(limit).ToList() 
                : allEvents.ToList();
        }

        var index = allEvents.ToList().FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
        
        if (index < 0)
        {
            return new List<IEvent>();
        }

        var deltaEvents = allEvents.Skip(index + 1);
        return limit > 0 
            ? deltaEvents.Take(limit).ToList() 
            : deltaEvents.ToList();
    }

    // Legacy method - kept for compatibility
    public async Task<IReadOnlyList<IEvent>> GetAllEventsAsync()
    {
        var retrievalInfo = GetEventRetrievalInfo();
        var events = await _eventReader.GetEvents(retrievalInfo).UnwrapBox();
        return events.ToList();
    }

    public async Task<string> GetLastSortableUniqueIdAsync()
    {
        var state = await StateManager.TryGetStateAsync<AggregateEventHandlerState>(StateKey);
        return state.HasValue ? state.Value.LastSortableUniqueId : string.Empty;
    }

    public Task RegisterProjectorAsync(string projectorKey)
    {
        // No-op for now - could be extended to track projector registrations
        return Task.CompletedTask;
    }
    
    // New envelope-based AppendEventsAsync
    public async Task<EventHandlingResponse> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        List<EventEnvelope> newEvents)
    {
        try
        {
            // Convert envelopes to events
            var events = new List<IEvent>();
            foreach (var envelope in newEvents)
            {
                try
                {
                    var eventType = Type.GetType(envelope.EventType);
                    if (eventType == null)
                    {
                        _logger.LogWarning("Event type not found: {EventType}", envelope.EventType);
                        continue;
                    }
                    
                    var eventJson = System.Text.Encoding.UTF8.GetString(envelope.EventPayload);
                    var @event = JsonSerializer.Deserialize(eventJson, eventType) as IEvent;
                    
                    if (@event != null)
                    {
                        events.Add(@event);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize event from envelope");
                }
            }
            
            // Use legacy method to append events
            var appendedEvents = await AppendEventsAsync(expectedLastSortableUniqueId, events);
            
            var lastEventId = appendedEvents.Any() ? appendedEvents.Last().SortableUniqueId : expectedLastSortableUniqueId;
            return EventHandlingResponse.Success(lastEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append events from envelopes");
            return EventHandlingResponse.Failure(ex.Message);
        }
    }
    
    // New envelope-based GetDeltaEventsAsync
    async Task<List<EventEnvelope>> IAggregateEventHandlerActor.GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        try
        {
            var events = await GetDeltaEventsAsync(fromSortableUniqueId, limit);
            return await ConvertEventsToEnvelopes(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get delta events as envelopes");
            return new List<EventEnvelope>();
        }
    }
    
    // New envelope-based GetAllEventsAsync
    async Task<List<EventEnvelope>> IAggregateEventHandlerActor.GetAllEventsAsync()
    {
        try
        {
            var events = await GetAllEventsAsync();
            return await ConvertEventsToEnvelopes(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all events as envelopes");
            return new List<EventEnvelope>();
        }
    }
    
    private async Task<List<EventEnvelope>> ConvertEventsToEnvelopes(IReadOnlyList<IEvent> events)
    {
        // Extract partition keys from actor ID
        var actorId = Id.GetId();
        var partitionKeysString = actorId.Contains(':') 
            ? actorId.Substring(actorId.IndexOf(':') + 1) 
            : actorId;
        
        var partitionKeys = PartitionKeys
            .FromPrimaryKeysString(partitionKeysString)
            .UnwrapBox();
        
        var envelopes = new List<EventEnvelope>();
        
        foreach (var @event in events)
        {
            var envelope = new EventEnvelope
            {
                EventType = @event.GetType().FullName ?? @event.GetType().Name,
                EventPayload = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event)),
                AggregateId = partitionKeys.AggregateId.ToString(),
                PartitionId = partitionKeys.AggregateId,
                RootPartitionKey = partitionKeys.RootPartitionKey,
                Version = @event.Version,
                SortableUniqueId = @event.GetSortableUniqueId(),
                Timestamp = DateTime.UtcNow, // Could extract from sortableUniqueId
                Metadata = new Dictionary<string, string>(),
                CorrelationId = string.Empty,
                CausationId = string.Empty
            };
            
            envelopes.Add(envelope);
        }
        
        return envelopes;
    }

    private EventRetrievalInfo GetEventRetrievalInfo()
    {
        // Extract partition keys from actor ID
        var actorId = Id.GetId();
        var partitionKeysString = actorId.Contains(':') 
            ? actorId.Substring(actorId.IndexOf(':') + 1) 
            : actorId;
        
        return PartitionKeys
            .FromPrimaryKeysString(partitionKeysString)
            .Remap(EventRetrievalInfo.FromPartitionKeys)
            .UnwrapBox();
    }

    /// <summary>
    /// State object for persisting event handler information
    /// </summary>
    private record AggregateEventHandlerState
    {
        public string LastSortableUniqueId { get; init; } = string.Empty;
        public OptionalValue<DateTime> LastEventDate { get; init; } = OptionalValue<DateTime>.Empty;
    }
}