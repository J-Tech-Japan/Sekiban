using Dapr.Actors;
using Dapr.Actors.Runtime;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using ResultBoxes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sekiban.Pure;

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
    // State key for persisting serializable event documents (for in-memory storage)
    private const string EventDocumentsStateKey = "aggregateEventDocuments";
    
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
            // Note: We store EventEnvelopes in the new AppendEventsAsync method
            // Here we just try to save to external storage if available
            try
            {
                await _eventWriter.SaveEvents(toStoreEvents);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save events to external storage, using in-memory only");
            }
            
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
        // Convert from serializable documents to maintain compatibility
        var documents = await ((IAggregateEventHandlerActor)this).GetDeltaEventsAsync(fromSortableUniqueId, limit);
        
        var events = new List<IEvent>();
        foreach (var document in documents)
        {
            try
            {
                var eventResult = await document.ToEventAsync(_sekibanDomainTypes);
                if (eventResult.HasValue)
                {
                    events.Add(eventResult.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert document to event");
            }
        }
        
        return events;
    }

    // Legacy method - kept for compatibility
    public async Task<IReadOnlyList<IEvent>> GetAllEventsAsync()
    {
        // Delegate to GetDeltaEventsAsync with no limit
        return await GetDeltaEventsAsync(string.Empty, -1);
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
    
    // New SerializableEventDocument-based AppendEventsAsync
    public async Task<EventHandlingResponse> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        List<SerializableEventDocument> newEventDocuments)
    {
        try
        {
            // Convert serializable event documents to events
            var events = new List<IEvent>();
            foreach (var eventDoc in newEventDocuments)
            {
                try
                {
                    var eventResult = await eventDoc.ToEventAsync(_sekibanDomainTypes);
                    if (eventResult.HasValue)
                    {
                        events.Add(eventResult.Value);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to convert SerializableEventDocument to event for type: {PayloadTypeName}", eventDoc.PayloadTypeName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize event from document");
                }
            }
            
            // Store serializable event documents directly in actor state for in-memory storage
            var existingEventDocs = await StateManager.TryGetStateAsync<List<SerializableEventDocument>>(EventDocumentsStateKey);
            var eventDocsList = existingEventDocs.HasValue ? existingEventDocs.Value : new List<SerializableEventDocument>();
            eventDocsList.AddRange(newEventDocuments);
            await StateManager.SetStateAsync(EventDocumentsStateKey, eventDocsList);
            
            // Use legacy method to append events
            var appendedEvents = await AppendEventsAsync(expectedLastSortableUniqueId, events);
            
            var lastEventId = newEventDocuments.Any() ? newEventDocuments.Last().SortableUniqueId : expectedLastSortableUniqueId;
            return EventHandlingResponse.Success(lastEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append events from envelopes");
            return EventHandlingResponse.Failure(ex.Message);
        }
    }
    
    // New SerializableEventDocument-based GetDeltaEventsAsync
    async Task<List<SerializableEventDocument>> IAggregateEventHandlerActor.GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        try
        {
            // First try to get event documents from actor state (in-memory)
            var storedEventDocs = await StateManager.TryGetStateAsync<List<SerializableEventDocument>>(EventDocumentsStateKey);
            if (storedEventDocs.HasValue && storedEventDocs.Value.Any())
            {
                var allEventDocs = storedEventDocs.Value;
                
                if (string.IsNullOrWhiteSpace(fromSortableUniqueId))
                {
                    return limit > 0 
                        ? allEventDocs.Take(limit).ToList() 
                        : allEventDocs;
                }

                var index = allEventDocs.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
                
                if (index < 0)
                {
                    return new List<SerializableEventDocument>();
                }

                var deltaEventDocs = allEventDocs.Skip(index + 1);
                return limit > 0 
                    ? deltaEventDocs.Take(limit).ToList() 
                    : deltaEventDocs.ToList();
            }
            
            // Fall back to converting from legacy events
            var events = await GetDeltaEventsAsync(fromSortableUniqueId, limit);
            return await ConvertEventsToSerializableDocuments(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get delta events as documents");
            return new List<SerializableEventDocument>();
        }
    }
    
    // New SerializableEventDocument-based GetAllEventsAsync
    async Task<List<SerializableEventDocument>> IAggregateEventHandlerActor.GetAllEventsAsync()
    {
        try
        {
            // First try to get event documents from actor state (in-memory)
            var storedEventDocs = await StateManager.TryGetStateAsync<List<SerializableEventDocument>>(EventDocumentsStateKey);
            if (storedEventDocs.HasValue && storedEventDocs.Value.Any())
            {
                return storedEventDocs.Value;
            }
            
            // Fall back to converting from legacy events
            var events = await GetAllEventsAsync();
            return await ConvertEventsToSerializableDocuments(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all events as documents");
            return new List<SerializableEventDocument>();
        }
    }
    
    private async Task<List<SerializableEventDocument>> ConvertEventsToSerializableDocuments(IReadOnlyList<IEvent> events)
    {
        // Extract partition keys from actor ID
        var actorId = Id.GetId();
        var partitionKeysString = actorId.Contains(':') 
            ? actorId.Substring(actorId.IndexOf(':') + 1) 
            : actorId;
        
        var partitionKeys = PartitionKeys
            .FromPrimaryKeysString(partitionKeysString)
            .UnwrapBox();
        
        var documents = new List<SerializableEventDocument>();
        
        foreach (var @event in events)
        {
            // Convert event to SerializableEventDocument
            var document = await SerializableEventDocument.CreateFromEventAsync(@event, _sekibanDomainTypes.JsonSerializerOptions);
            documents.Add(document);
        }
        
        return documents;
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