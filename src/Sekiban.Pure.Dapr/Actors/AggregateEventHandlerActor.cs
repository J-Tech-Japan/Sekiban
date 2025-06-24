using Dapr.Actors;
using Dapr.Actors.Runtime;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using ResultBoxes;

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
    
    // State key for persisting the last event information
    private const string StateKey = "aggregateEventHandler";
    
    public AggregateEventHandlerActor(
        ActorHost host,
        IEventWriter eventWriter,
        IEventReader eventReader,
        SekibanDomainTypes sekibanDomainTypes) : base(host)
    {
        _eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));
        _eventReader = eventReader ?? throw new ArgumentNullException(nameof(eventReader));
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
    }

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

    public async Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(string fromSortableUniqueId, int? limit = null)
    {
        var retrievalInfo = GetEventRetrievalInfo();
        var allEvents = await _eventReader.GetEvents(retrievalInfo).UnwrapBox();
        
        if (string.IsNullOrWhiteSpace(fromSortableUniqueId))
        {
            return limit.HasValue 
                ? allEvents.Take(limit.Value).ToList() 
                : allEvents.ToList();
        }

        var index = allEvents.ToList().FindIndex(e => e.SortableUniqueId == fromSortableUniqueId);
        
        if (index < 0)
        {
            return new List<IEvent>();
        }

        var deltaEvents = allEvents.Skip(index + 1);
        return limit.HasValue 
            ? deltaEvents.Take(limit.Value).ToList() 
            : deltaEvents.ToList();
    }

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