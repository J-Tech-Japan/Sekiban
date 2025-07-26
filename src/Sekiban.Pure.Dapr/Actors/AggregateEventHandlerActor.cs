using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Client;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Dapr.Serialization;
using ResultBoxes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Pure.Dapr.Configuration;

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
    private readonly DaprClient _daprClient;
    private readonly DaprSekibanOptions _options;
    private readonly IDaprSerializationService _serialization;
    private readonly ILogger<AggregateEventHandlerActor> _logger;
    
    private const string StateKey = "aggregateEventHandler";
    private const string EventDocumentsStateKey = "aggregateEventDocuments";
    private const string PubSubName = "sekiban-pubsub";
    private const string EventTopicName = "events.all";
    
    /// <summary>
    /// Initializes a new instance of the AggregateEventHandlerActor class.
    /// </summary>
    public AggregateEventHandlerActor(
        ActorHost host,
        IEventWriter eventWriter,
        IEventReader eventReader,
        SekibanDomainTypes sekibanDomainTypes,
        DaprClient daprClient,
        IOptions<DaprSekibanOptions> options,
        IDaprSerializationService serialization,
        ILogger<AggregateEventHandlerActor> logger) : base(host)
    {
        _eventWriter = eventWriter ?? throw new ArgumentNullException(nameof(eventWriter));
        _eventReader = eventReader ?? throw new ArgumentNullException(nameof(eventReader));
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Appends new events to the aggregate stream with optimistic concurrency control.
    /// </summary>
    public async Task<EventHandlingResponse> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        List<SerializableEventDocument> newEventDocuments)
    {
        try
        {
            var persistedState = await StateManager.TryGetStateAsync<AggregateEventHandlerState>(StateKey);
            var currentState = persistedState.HasValue 
                ? persistedState.Value 
                : new AggregateEventHandlerState { LastSortableUniqueId = string.Empty };

            if (!string.IsNullOrWhiteSpace(expectedLastSortableUniqueId) && 
                currentState.LastSortableUniqueId != expectedLastSortableUniqueId)
            {
                throw new InvalidOperationException(
                    $"Expected last event ID '{expectedLastSortableUniqueId}' does not match actual '{currentState.LastSortableUniqueId}'");
            }

            var events = new List<IEvent>();
            foreach (var eventDoc in newEventDocuments)
            {
                try
                {
                    var eventResult = await eventDoc.ToEventAsync(_sekibanDomainTypes);
                    if (eventResult.HasValue && eventResult.Value != null)
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

            var toStoreEvents = events.ToEventsAndReplaceTime(_sekibanDomainTypes.EventTypes.GetEventTypes());
            
            var existingEventDocs = await StateManager.TryGetStateAsync<List<SerializableEventDocument>>(EventDocumentsStateKey);
            var eventDocsList = existingEventDocs.HasValue ? existingEventDocs.Value : new List<SerializableEventDocument>();
            eventDocsList.AddRange(newEventDocuments);
            await StateManager.SetStateAsync(EventDocumentsStateKey, eventDocsList);

            if (toStoreEvents.Count > 0)
            {
                try
                {
                    await _eventWriter.SaveEvents(toStoreEvents);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save events to external storage, using in-memory only");
                }
                
                var lastEvent = toStoreEvents.Last();
                var newState = new AggregateEventHandlerState
                {
                    LastSortableUniqueId = lastEvent.SortableUniqueId,
                    LastEventDate = new SortableUniqueIdValue(lastEvent.SortableUniqueId).GetTicks()
                };
                
                await StateManager.SetStateAsync(StateKey, newState);
                
                // Publish events to PubSub
                await PublishEventsToPubSub(newEventDocuments);
            }
            
            var lastEventId = newEventDocuments.Any() ? newEventDocuments.Last().SortableUniqueId : expectedLastSortableUniqueId;
            return EventHandlingResponse.Success(lastEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append events from envelopes");
            return EventHandlingResponse.Failure(ex.Message);
        }
    }
    
    /// <summary>
    /// Gets delta events from a specific point in the stream.
    /// </summary>
    public async Task<List<SerializableEventDocument>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        try
        {
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
            
            try
            {
                var eventRetrievalInfo = GetEventRetrievalInfo();
                var eventsResult = await _eventReader.GetEvents(eventRetrievalInfo);
                
                if (!eventsResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to retrieve events from external storage: {Error}", eventsResult.GetException().Message);
                    return new List<SerializableEventDocument>();
                }

                var events = eventsResult.GetValue();
                
                var filteredEvents = events.AsEnumerable();
                
                if (!string.IsNullOrWhiteSpace(fromSortableUniqueId))
                {
                    filteredEvents = filteredEvents
                        .SkipWhile(e => e.SortableUniqueId != fromSortableUniqueId)
                        .Skip(1);
                }
                
                if (limit > 0)
                {
                    filteredEvents = filteredEvents.Take(limit);
                }
                
                return await ConvertEventsToSerializableDocuments(filteredEvents.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve events from external storage, returning empty list");
                return new List<SerializableEventDocument>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get delta events as documents");
            return new List<SerializableEventDocument>();
        }
    }
    
    /// <summary>
    /// Gets all events for the aggregate stream.
    /// </summary>
    public async Task<List<SerializableEventDocument>> GetAllEventsAsync()
    {
        try
        {
            var storedEventDocs = await StateManager.TryGetStateAsync<List<SerializableEventDocument>>(EventDocumentsStateKey);
            if (storedEventDocs.HasValue && storedEventDocs.Value.Any())
            {
                return storedEventDocs.Value;
            }
            
            return await GetDeltaEventsAsync(string.Empty, -1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all events as documents");
            return new List<SerializableEventDocument>();
        }
    }

    /// <summary>
    /// Gets the last sortable unique ID in the stream.
    /// </summary>
    public async Task<string> GetLastSortableUniqueIdAsync()
    {
        var state = await StateManager.TryGetStateAsync<AggregateEventHandlerState>(StateKey);
        return state.HasValue ? state.Value.LastSortableUniqueId : string.Empty;
    }

    /// <summary>
    /// Registers a projector with this event handler (optional).
    /// </summary>
    public Task RegisterProjectorAsync(string projectorKey)
    {
        return Task.CompletedTask;
    }
    
    private async Task<List<SerializableEventDocument>> ConvertEventsToSerializableDocuments(IReadOnlyList<IEvent> events)
    {
        var documents = new List<SerializableEventDocument>();
        
        foreach (var @event in events)
        {
            var document = await SerializableEventDocument.CreateFromEventAsync(@event, _sekibanDomainTypes.JsonSerializerOptions);
            documents.Add(document);
        }
        
        return documents;
    }

    private EventRetrievalInfo GetEventRetrievalInfo()
    {
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
    /// Publishes events to Dapr PubSub for consumption by projectors
    /// </summary>
    private async Task PublishEventsToPubSub(List<SerializableEventDocument> eventDocuments)
    {
        foreach (var eventDoc in eventDocuments)
        {
            try
            {
                // Create DaprEventEnvelope from SerializableEventDocument
                var envelope = new DaprEventEnvelope
                {
                    EventId = eventDoc.Id,
                    EventData = eventDoc.CompressedPayloadJson, // Use compressed payload
                    EventType = eventDoc.PayloadTypeName,
                    AggregateId = eventDoc.AggregateId,
                    Version = eventDoc.Version,
                    Timestamp = eventDoc.TimeStamp,
                    SortableUniqueId = eventDoc.SortableUniqueId,
                    RootPartitionKey = eventDoc.RootPartitionKey,
                    IsCompressed = true, // SerializableEventDocument stores compressed data
                    Metadata = new Dictionary<string, string>
                    {
                        ["PartitionGroup"] = eventDoc.AggregateGroup,
                        ["ActorId"] = Id.GetId()
                    }
                };
                
                // Publish to PubSub
                await _daprClient.PublishEventAsync(PubSubName, EventTopicName, envelope);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish event to PubSub: EventId={EventId}", eventDoc.Id);
                // Continue with other events even if one fails
            }
        }
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