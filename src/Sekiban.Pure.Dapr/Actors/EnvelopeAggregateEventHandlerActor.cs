using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Dapr.EventStore;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.Collections.Concurrent;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor for handling event persistence and retrieval using envelope-based communication.
/// This implementation uses EventEnvelopes for proper Dapr JSON serialization.
/// </summary>
[Actor(TypeName = nameof(EnvelopeAggregateEventHandlerActor))]
public class EnvelopeAggregateEventHandlerActor : Actor, IAggregateEventHandlerActor
{
    private readonly DaprClient _daprClient;
    private readonly IEnvelopeProtobufService _envelopeService;
    private readonly ILogger<EnvelopeAggregateEventHandlerActor> _logger;
    private readonly string _stateStoreName;
    
    private const string EventListStateKey = "eventList";
    private const string MetadataStateKey = "metadata";
    private const string ProjectorRegistrationKey = "projectorRegistrations";
    
    // In-memory cache for performance
    private List<EventEnvelope> _eventEnvelopes = new();
    private EventHandlerMetadata _metadata = new();
    private readonly HashSet<string> _registeredProjectors = new();

    public EnvelopeAggregateEventHandlerActor(
        ActorHost host,
        DaprClient daprClient,
        IEnvelopeProtobufService envelopeService,
        ILogger<EnvelopeAggregateEventHandlerActor> logger,
        string stateStoreName = "sekiban-eventstore") : base(host)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _envelopeService = envelopeService ?? throw new ArgumentNullException(nameof(envelopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateStoreName = stateStoreName;
    }

    protected override async Task OnActivateAsync()
    {
        await base.OnActivateAsync();
        
        try
        {
            // Load existing events
            var storedEnvelopes = await StateManager.TryGetStateAsync<List<EventEnvelope>>(EventListStateKey);
            if (storedEnvelopes.HasValue)
            {
                _eventEnvelopes = storedEnvelopes.Value;
                _logger.LogDebug("Loaded {EventCount} events from state", _eventEnvelopes.Count);
            }
            
            // Load metadata
            var storedMetadata = await StateManager.TryGetStateAsync<EventHandlerMetadata>(MetadataStateKey);
            if (storedMetadata.HasValue)
            {
                _metadata = storedMetadata.Value;
            }
            
            // Load registered projectors
            var storedProjectors = await StateManager.TryGetStateAsync<HashSet<string>>(ProjectorRegistrationKey);
            if (storedProjectors.HasValue)
            {
                foreach (var projector in storedProjectors.Value)
                {
                    _registeredProjectors.Add(projector);
                }
            }
            
            // Register timer for periodic state saving
            await RegisterTimerAsync(
                "SaveState",
                nameof(SaveStateCallbackAsync),
                null,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during event handler actor activation");
            throw;
        }
    }

    protected override async Task OnDeactivateAsync()
    {
        // Save any pending changes
        await SaveStateAsync();
        await base.OnDeactivateAsync();
    }

    public async Task<EventHandlingResponse> AppendEventsAsync(
        string expectedLastSortableUniqueId, 
        List<EventEnvelope> newEventEnvelopes)
    {
        try
        {
            _logger.LogDebug("Appending {EventCount} events, expected last ID: {ExpectedId}",
                newEventEnvelopes.Count, expectedLastSortableUniqueId);

            // Optimistic concurrency check
            var currentLastId = _eventEnvelopes.LastOrDefault()?.SortableUniqueId ?? string.Empty;
            if (currentLastId != expectedLastSortableUniqueId)
            {
                _logger.LogWarning("Concurrency conflict: expected {Expected}, actual {Actual}",
                    expectedLastSortableUniqueId, currentLastId);
                
                return EventHandlingResponse.Failure(
                    $"Concurrency conflict: expected last event ID '{expectedLastSortableUniqueId}' but found '{currentLastId}'");
            }

            // Append new events
            foreach (var envelope in newEventEnvelopes)
            {
                _eventEnvelopes.Add(envelope);
                
                // Also persist to external store if configured
                await PersistEventToExternalStore(envelope);
            }

            // Update metadata
            _metadata.LastEventId = newEventEnvelopes.Last().SortableUniqueId;
            _metadata.LastEventVersion = newEventEnvelopes.Last().Version;
            _metadata.TotalEventCount = _eventEnvelopes.Count;
            _metadata.LastModified = DateTime.UtcNow;

            // Save state
            await SaveStateAsync();

            // Publish events for subscribers
            foreach (var envelope in newEventEnvelopes)
            {
                await PublishEvent(envelope);
            }

            return EventHandlingResponse.Success(_metadata.LastEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error appending events");
            return EventHandlingResponse.Failure($"Error appending events: {ex.Message}");
        }
    }

    public async Task<List<EventEnvelope>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit)
    {
        try
        {
            _logger.LogDebug("Getting delta events from {FromId} with limit {Limit}",
                fromSortableUniqueId, limit);

            var startIndex = 0;
            if (!string.IsNullOrEmpty(fromSortableUniqueId))
            {
                startIndex = _eventEnvelopes.FindIndex(e => e.SortableUniqueId == fromSortableUniqueId) + 1;
                if (startIndex == 0) // Not found
                {
                    _logger.LogWarning("Starting event ID {FromId} not found", fromSortableUniqueId);
                    startIndex = 0;
                }
            }

            var events = new List<EventEnvelope>();
            var count = 0;
            
            for (int i = startIndex; i < _eventEnvelopes.Count; i++)
            {
                events.Add(_eventEnvelopes[i]);
                count++;
                
                if (limit > 0 && count >= limit)
                {
                    break;
                }
            }

            _logger.LogDebug("Returning {EventCount} delta events", events.Count);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting delta events");
            throw;
        }
    }

    public async Task<List<EventEnvelope>> GetAllEventsAsync()
    {
        try
        {
            _logger.LogDebug("Getting all events (count: {Count})", _eventEnvelopes.Count);
            return new List<EventEnvelope>(_eventEnvelopes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all events");
            throw;
        }
    }

    public async Task<string> GetLastSortableUniqueIdAsync()
    {
        try
        {
            var lastId = _eventEnvelopes.LastOrDefault()?.SortableUniqueId ?? string.Empty;
            _logger.LogDebug("Last sortable unique ID: {LastId}", lastId);
            return lastId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last sortable unique ID");
            throw;
        }
    }

    public async Task RegisterProjectorAsync(string projectorKey)
    {
        try
        {
            _logger.LogDebug("Registering projector: {ProjectorKey}", projectorKey);
            
            if (_registeredProjectors.Add(projectorKey))
            {
                await StateManager.SetStateAsync(ProjectorRegistrationKey, _registeredProjectors);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering projector {ProjectorKey}", projectorKey);
            throw;
        }
    }

    private async Task SaveStateAsync()
    {
        try
        {
            // Save events
            await StateManager.SetStateAsync(EventListStateKey, _eventEnvelopes);
            
            // Save metadata
            await StateManager.SetStateAsync(MetadataStateKey, _metadata);
            
            _logger.LogDebug("State saved: {EventCount} events", _eventEnvelopes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving state");
            throw;
        }
    }

    public async Task SaveStateCallbackAsync(object? state)
    {
        await SaveStateAsync();
    }

    private async Task PersistEventToExternalStore(EventEnvelope envelope)
    {
        try
        {
            // Generate storage key
            var key = $"events:{envelope.RootPartitionKey}:{envelope.AggregateId}:v{envelope.Version}";
            
            // Save to Dapr state store
            await _daprClient.SaveStateAsync(_stateStoreName, key, envelope);
            
            _logger.LogDebug("Persisted event to external store: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist event to external store");
            // Don't fail the operation - external persistence is optional
        }
    }

    private async Task PublishEvent(EventEnvelope envelope)
    {
        try
        {
            // Publish to event-specific topic
            var topicName = $"events.{envelope.EventType.Split('.').Last()}";
            await _daprClient.PublishEventAsync("sekiban-pubsub", topicName, envelope);
            
            // Also publish to general events topic
            await _daprClient.PublishEventAsync("sekiban-pubsub", "events.all", envelope);
            
            _logger.LogDebug("Published event {EventType} to topics", envelope.EventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish event");
            // Don't fail the operation - publishing is optional
        }
    }

    /// <summary>
    /// Metadata about the event handler state
    /// </summary>
    private record EventHandlerMetadata
    {
        public string LastEventId { get; set; } = string.Empty;
        public int LastEventVersion { get; set; }
        public int TotalEventCount { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}