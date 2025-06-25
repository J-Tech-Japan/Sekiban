using Dapr.Client;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Dapr.Protos;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.EventStore;

/// <summary>
/// Protobuf-enabled Dapr state store implementation of event store
/// Stores events as Protobuf messages for efficient serialization
/// </summary>
public class ProtobufDaprEventStore : Repository, IEventWriter, IEventReader
{
    private readonly DaprClient _daprClient;
    private readonly IDaprProtobufSerializationService _serialization;
    private readonly ILogger<ProtobufDaprEventStore> _logger;
    private readonly string _stateStoreName;
    private readonly string _pubSubName;
    private readonly bool _useJsonFallback;

    public ProtobufDaprEventStore(
        DaprClient daprClient,
        IDaprProtobufSerializationService serialization,
        ILogger<ProtobufDaprEventStore> logger,
        string stateStoreName = "sekiban-eventstore",
        string pubSubName = "sekiban-pubsub",
        bool useJsonFallback = true)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stateStoreName = stateStoreName;
        _pubSubName = pubSubName;
        _useJsonFallback = useJsonFallback;
        
        // Set up serializer and deserializer for base Repository
        Serializer = (evt) => JsonSerializer.Serialize(evt);
        Deserializer = (json) => JsonSerializer.Deserialize<IEvent>(json)!;
    }

    /// <summary>
    /// Saves events to Dapr state store using Protobuf serialization
    /// </summary>
    public async Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        var eventsList = events.ToList();
        
        foreach (var @event in eventsList)
        {
            try
            {
                // Create protobuf event envelope
                var protobufEnvelope = await _serialization.SerializeEventToProtobufAsync(
                    @event,
                    @event.PartitionKeys.AggregateId,
                    @event.Version,
                    @event.PartitionKeys.RootPartitionKey);

                // Generate state key
                var key = GenerateEventKey(@event.PartitionKeys, @event.Version);

                // Save protobuf bytes to Dapr state store
                await _daprClient.SaveStateAsync(_stateStoreName, key, protobufEnvelope.ToByteArray());
                
                // Save metadata about the format
                await SaveEventMetadata(@event, isProtobuf: true);

                // Publish event for subscribers (using protobuf)
                await PublishProtobufEvent(@event, protobufEnvelope);
                
                // If configured, also save JSON version for compatibility
                if (_useJsonFallback)
                {
                    await SaveJsonVersion(@event);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save event {EventId} for aggregate {AggregateId}", 
                    @event.Id, @event.PartitionKeys.AggregateId);
                throw;
            }
        }
        
        // Also save to in-memory repository
        base.Save(eventsList.Cast<IEvent>().ToList());
    }

    /// <summary>
    /// Retrieves events from Dapr state store with Protobuf deserialization
    /// </summary>
    public async Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo)
    {
        try
        {
            var events = new List<IEvent>();
            
            if (eventRetrievalInfo.AggregateId.HasValue)
            {
                var partitionKeys = new PartitionKeys(
                    eventRetrievalInfo.AggregateId.Value,
                    string.Empty,
                    eventRetrievalInfo.RootPartitionKey.GetValue() ?? string.Empty);
                
                // Get metadata to know the latest version
                var metadata = await GetAggregateMetadata(partitionKeys);
                var maxVersion = metadata?.LatestVersion ?? 1000; // Fallback to scanning up to 1000
                
                var version = 1;
                var foundEvents = 0;
                
                while (version <= maxVersion)
                {
                    var key = GenerateEventKey(partitionKeys, version);
                    
                    try
                    {
                        // Try to load as protobuf first
                        var protobufBytes = await _daprClient.GetStateAsync<byte[]>(_stateStoreName, key);
                        
                        if (protobufBytes == null || protobufBytes.Length == 0)
                        {
                            // No event at this version
                            version++;
                            continue;
                        }
                        
                        IEvent? @event = null;
                        
                        try
                        {
                            // Try to parse as protobuf
                            var protobufEnvelope = ProtobufEventEnvelope.Parser.ParseFrom(protobufBytes);
                            @event = await _serialization.DeserializeEventFromProtobufAsync(protobufEnvelope);
                        }
                        catch (Exception)
                        {
                            // Failed to parse as protobuf, try JSON fallback if enabled
                            if (_useJsonFallback)
                            {
                                try
                                {
                                    var jsonEnvelope = await _daprClient.GetStateAsync<DaprEventEnvelope>(_stateStoreName, key);
                                    if (jsonEnvelope != null)
                                    {
                                        @event = await _serialization.DeserializeEventAsync(jsonEnvelope);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to deserialize event at key {Key} as JSON", key);
                                }
                            }
                        }
                        
                        if (@event != null)
                        {
                            // Check sortable ID condition
                            if (eventRetrievalInfo.SortableIdCondition != null)
                            {
                                var sortableIdValue = new SortableUniqueIdValue(@event.GetSortableUniqueId());
                                if (eventRetrievalInfo.SortableIdCondition.OutsideOfRange(sortableIdValue))
                                {
                                    version++;
                                    continue;
                                }
                            }
                            
                            events.Add(@event);
                            foundEvents++;
                            
                            if (eventRetrievalInfo.MaxCount.HasValue && foundEvents >= eventRetrievalInfo.MaxCount.Value)
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load event at key {Key}", key);
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
    /// Override Save method to persist to Dapr using Protobuf
    /// </summary>
    public new ResultBox<List<IEvent>> Save(List<IEvent> events)
    {
        // Save to Dapr asynchronously
        Task.Run(async () => await SaveEvents(events)).Wait();
        
        // Also save to in-memory store
        return base.Save(events);
    }

    /// <summary>
    /// Batch save operation for better performance
    /// </summary>
    public async Task<ResultBox<List<IEvent>>> SaveEventsBatchAsync(List<IEvent> events)
    {
        try
        {
            // Group events by aggregate for better performance
            var eventsByAggregate = events.GroupBy(e => e.PartitionKeys.AggregateId);
            
            var saveTasks = new List<Task>();
            
            foreach (var aggregateEvents in eventsByAggregate)
            {
                // Create a batch save operation for each aggregate
                var batchItems = new List<BulkStateItem>();
                
                foreach (var @event in aggregateEvents.OrderBy(e => e.Version))
                {
                    var protobufEnvelope = await _serialization.SerializeEventToProtobufAsync(
                        @event,
                        @event.PartitionKeys.AggregateId,
                        @event.Version,
                        @event.PartitionKeys.RootPartitionKey);
                    
                    var key = GenerateEventKey(@event.PartitionKeys, @event.Version);
                    
                    batchItems.Add(new BulkStateItem(
                        key,
                        protobufEnvelope.ToByteArray(),
                        null)); // No etag for new events
                }
                
                // Save batch
                saveTasks.Add(_daprClient.SaveBulkStateAsync(_stateStoreName, batchItems));
            }
            
            await Task.WhenAll(saveTasks);
            
            // Update metadata and publish events
            foreach (var @event in events)
            {
                await SaveEventMetadata(@event, isProtobuf: true);
                
                var protobufEnvelope = await _serialization.SerializeEventToProtobufAsync(
                    @event,
                    @event.PartitionKeys.AggregateId,
                    @event.Version,
                    @event.PartitionKeys.RootPartitionKey);
                    
                await PublishProtobufEvent(@event, protobufEnvelope);
            }
            
            // Also save to in-memory store
            return base.Save(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch save events");
            return ResultBox<List<IEvent>>.FromException(ex);
        }
    }

    private string GenerateEventKey(PartitionKeys partitionKeys, int version)
    {
        return $"events:{partitionKeys.RootPartitionKey}:{partitionKeys.AggregateId}:v{version}";
    }

    private async Task SaveEventMetadata(IEvent @event, bool isProtobuf)
    {
        var metadataKey = $"events:{@event.PartitionKeys.RootPartitionKey}:{@event.PartitionKeys.AggregateId}:metadata";
        await _daprClient.SaveStateAsync(_stateStoreName, metadataKey, new AggregateMetadata
        {
            LatestVersion = @event.Version,
            LastEventId = @event.GetSortableUniqueId(),
            LastModified = DateTime.UtcNow,
            StorageFormat = isProtobuf ? "protobuf" : "json",
            SerializerVersion = "2.0"
        });
    }

    private async Task<AggregateMetadata?> GetAggregateMetadata(PartitionKeys partitionKeys)
    {
        var metadataKey = $"events:{partitionKeys.RootPartitionKey}:{partitionKeys.AggregateId}:metadata";
        return await _daprClient.GetStateAsync<AggregateMetadata>(_stateStoreName, metadataKey);
    }

    private async Task PublishProtobufEvent(IEvent @event, ProtobufEventEnvelope protobufEnvelope)
    {
        // Publish protobuf version to specific topic
        var protobufTopicName = $"protobuf.events.{@event.GetType().Name}";
        await _daprClient.PublishEventAsync(_pubSubName, protobufTopicName, protobufEnvelope.ToByteArray());
        
        // Also publish to general protobuf events topic
        await _daprClient.PublishEventAsync(_pubSubName, "protobuf.events.all", protobufEnvelope.ToByteArray());
        
        // For backward compatibility, also publish JSON version
        if (_useJsonFallback)
        {
            var jsonEnvelope = await _serialization.SerializeEventAsync(
                @event,
                @event.PartitionKeys.AggregateId,
                @event.Version,
                @event.PartitionKeys.RootPartitionKey);
                
            var topicName = $"events.{@event.GetType().Name}";
            await _daprClient.PublishEventAsync(_pubSubName, topicName, jsonEnvelope);
            await _daprClient.PublishEventAsync(_pubSubName, "events.all", jsonEnvelope);
        }
    }

    private async Task SaveJsonVersion(IEvent @event)
    {
        try
        {
            var jsonEnvelope = await _serialization.SerializeEventAsync(
                @event,
                @event.PartitionKeys.AggregateId,
                @event.Version,
                @event.PartitionKeys.RootPartitionKey);
            
            var jsonKey = $"{GenerateEventKey(@event.PartitionKeys, @event.Version)}.json";
            await _daprClient.SaveStateAsync(_stateStoreName, jsonKey, jsonEnvelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save JSON version of event {EventId}", @event.Id);
            // Don't throw - this is just for compatibility
        }
    }

    /// <summary>
    /// Metadata stored for each aggregate to track versions and format
    /// </summary>
    private record AggregateMetadata
    {
        public int LatestVersion { get; init; }
        public string LastEventId { get; init; } = string.Empty;
        public DateTime LastModified { get; init; }
        public string StorageFormat { get; init; } = "json";
        public string SerializerVersion { get; init; } = "1.0";
    }
}