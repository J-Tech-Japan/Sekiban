using Dapr.Client;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.EventStore;

/// <summary>
/// Dapr state store implementation of event store using the new serialization system
/// </summary>
public class DaprEventStore : Repository, IEventWriter, IEventReader
{
    private readonly DaprClient _daprClient;
    private readonly IDaprSerializationService _serialization;
    private readonly ILogger<DaprEventStore> _logger;
    private readonly string _stateStoreName;
    private readonly string _pubSubName;

    public DaprEventStore(
        DaprClient daprClient,
        IDaprSerializationService serialization,
        ILogger<DaprEventStore> logger,
        string stateStoreName = "sekiban-eventstore",
        string pubSubName = "sekiban-pubsub")
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateStoreName = stateStoreName;
        _pubSubName = pubSubName;
        
        // Set up serializer and deserializer for base Repository
        Serializer = (evt) => JsonSerializer.Serialize(evt);
        Deserializer = (json) => JsonSerializer.Deserialize<IEvent>(json)!;
    }

    /// <summary>
    /// Implementation of IEventWriter - saves events to Dapr state store
    /// </summary>
    public async Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        var eventsList = events.ToList();
        
        foreach (var @event in eventsList)
        {
            try
            {
                // Create event envelope
                var envelope = await _serialization.SerializeEventAsync(
                    @event,
                    @event.PartitionKeys.AggregateId,
                    @event.Version,
                    @event.PartitionKeys.RootPartitionKey);

                // Generate state key
                var key = GenerateEventKey(@event.PartitionKeys, @event.Version);

                // Save to Dapr state store
                await _daprClient.SaveStateAsync(_stateStoreName, key, envelope);
                
                // Update metadata
                await UpdateAggregateMetadata(@event);

                // Publish event for subscribers
                await PublishEvent(@event, envelope);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save event {EventId}", @event.Id);
                throw;
            }
        }
        
        // Also save to in-memory repository
        base.Save(eventsList.Cast<IEvent>().ToList());
    }

    /// <summary>
    /// Implementation of IEventReader - retrieves events from Dapr state store
    /// </summary>
    public async Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo)
    {
        try
        {
            var events = new List<IEvent>();
            
            // For now, use a simple approach - try to load events by version
            // In production, you'd want to implement proper querying
            if (eventRetrievalInfo.AggregateId.HasValue)
            {
                var partitionKeys = new PartitionKeys(
                    eventRetrievalInfo.AggregateId.Value,
                    string.Empty,
                    eventRetrievalInfo.RootPartitionKey.GetValue() ?? string.Empty);
                
                var version = 1;
                var foundEvents = 0;
                
                while (true)
                {
                    var key = GenerateEventKey(partitionKeys, version);
                    var envelope = await _daprClient.GetStateAsync<DaprEventEnvelope>(_stateStoreName, key);
                    
                    if (envelope == null)
                    {
                        break;
                    }
                    
                    // Check sortable ID condition
                    if (eventRetrievalInfo.SortableIdCondition != null)
                    {
                        var sortableIdValue = new SortableUniqueIdValue(envelope.SortableUniqueId);
                        if (eventRetrievalInfo.SortableIdCondition.OutsideOfRange(sortableIdValue))
                        {
                            version++;
                            continue;
                        }
                    }
                    
                    var @event = await _serialization.DeserializeEventAsync(envelope);
                    if (@event != null)
                    {
                        events.Add(@event);
                        foundEvents++;
                        
                        if (eventRetrievalInfo.MaxCount.HasValue && foundEvents >= eventRetrievalInfo.MaxCount.Value)
                        {
                            break;
                        }
                    }
                    
                    version++;
                }
            }
            
            return ResultBox.FromValue<IReadOnlyList<IEvent>>(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get events for retrieval info {Info}", eventRetrievalInfo);
            return ResultBox<IReadOnlyList<IEvent>>.FromException(ex);
        }
    }

    /// <summary>
    /// Override Save method to persist to Dapr
    /// </summary>
    public new ResultBox<List<IEvent>> Save(List<IEvent> events)
    {
        // Save to Dapr asynchronously
        Task.Run(async () => await SaveEvents(events)).Wait();
        
        // Also save to in-memory store
        return base.Save(events);
    }

    private string GenerateEventKey(PartitionKeys partitionKeys, int version)
    {
        return $"events:{partitionKeys.RootPartitionKey}:{partitionKeys.AggregateId}:v{version}";
    }

    private async Task UpdateAggregateMetadata(IEvent @event)
    {
        var metadataKey = $"events:{@event.PartitionKeys.RootPartitionKey}:{@event.PartitionKeys.AggregateId}:metadata";
        await _daprClient.SaveStateAsync(_stateStoreName, metadataKey, new AggregateMetadata
        {
            LatestVersion = @event.Version,
            LastEventId = @event.GetSortableUniqueId(),
            LastModified = DateTime.UtcNow
        });
    }

    private async Task PublishEvent(IEvent @event, DaprEventEnvelope envelope)
    {
        var topicName = $"events.{@event.GetType().Name}";
        await _daprClient.PublishEventAsync(_pubSubName, topicName, envelope);
        
        // Also publish to a general events topic
        await _daprClient.PublishEventAsync(_pubSubName, "events.all", envelope);
    }

    /// <summary>
    /// Metadata stored for each aggregate to track versions
    /// </summary>
    private record AggregateMetadata
    {
        public int LatestVersion { get; init; }
        public string LastEventId { get; init; } = string.Empty;
        public DateTime LastModified { get; init; }
    }
}