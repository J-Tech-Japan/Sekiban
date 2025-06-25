using Dapr.Client;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Keys;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Dapr.Serialization;

namespace Sekiban.Pure.Dapr.EventStore;

/// <summary>
/// Dapr state store implementation of event store using the new serialization system
/// </summary>
public class DaprEventStore : ISekibanRepository
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
    }

    public async Task<ResultBox<EventDocumentWithBlobData>> SaveEventAsync(
        EventDocument eventDocument,
        Type eventPayloadType)
    {
        try
        {
            // Create event envelope
            var envelope = await _serialization.SerializeEventAsync(
                eventDocument.Event,
                eventDocument.AggregateId,
                eventDocument.Version,
                eventDocument.RootPartitionKey);

            // Generate state key
            var key = GenerateEventKey(eventDocument);

            // Save to Dapr state store
            await _daprClient.SaveStateAsync(_stateStoreName, key, envelope);

            // Publish event for subscribers
            var topicName = $"events.{eventDocument.Event.GetType().Name}";
            await _daprClient.PublishEventAsync(_pubSubName, topicName, envelope);

            // Also publish to a general events topic
            await _daprClient.PublishEventAsync(_pubSubName, "events.all", envelope);

            return ResultBox.FromValue(new EventDocumentWithBlobData(eventDocument));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save event {EventId}", eventDocument.Id);
            return ResultBox<EventDocumentWithBlobData>.FromException(ex);
        }
    }

    public async Task<ResultBox<List<EventDocumentWithBlobData>>> GetEventsByPartitionKeysAsync(
        PartitionKeys partitionKeys,
        int? skip = null,
        int? limit = null)
    {
        try
        {
            var events = new List<EventDocumentWithBlobData>();
            
            // Query pattern for partition keys
            var keyPattern = GeneratePartitionKeyPattern(partitionKeys);
            
            // Note: Dapr doesn't have built-in query support for state stores
            // In production, you'd need to implement a proper indexing solution
            // For now, we'll simulate with a known range of versions
            
            var version = 1;
            var foundEvents = 0;
            var skippedEvents = 0;
            
            while (true)
            {
                var key = $"{keyPattern}:v{version}";
                var envelope = await _daprClient.GetStateAsync<DaprEventEnvelope>(_stateStoreName, key);
                
                if (envelope == null)
                {
                    // No more events
                    break;
                }
                
                if (skip.HasValue && skippedEvents < skip.Value)
                {
                    skippedEvents++;
                    version++;
                    continue;
                }
                
                var @event = await _serialization.DeserializeEventAsync(envelope);
                if (@event != null)
                {
                    var eventDocument = new EventDocument(
                        @event,
                        envelope.AggregateId,
                        envelope.RootPartitionKey,
                        envelope.Version);
                    
                    events.Add(new EventDocumentWithBlobData(eventDocument));
                    foundEvents++;
                    
                    if (limit.HasValue && foundEvents >= limit.Value)
                    {
                        break;
                    }
                }
                
                version++;
            }
            
            return ResultBox.FromValue(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get events for partition keys {PartitionKeys}", partitionKeys);
            return ResultBox<List<EventDocumentWithBlobData>>.FromException(ex);
        }
    }

    public async Task<ResultBox<EventDocumentWithBlobData?>> GetLatestEventByPartitionKeysAsync(
        PartitionKeys partitionKeys)
    {
        try
        {
            // Get metadata key
            var metadataKey = $"{GeneratePartitionKeyPattern(partitionKeys)}:metadata";
            var metadata = await _daprClient.GetStateAsync<AggregateMetadata>(_stateStoreName, metadataKey);
            
            if (metadata == null || metadata.LatestVersion == 0)
            {
                return ResultBox.FromValue<EventDocumentWithBlobData?>(null);
            }
            
            // Get the latest event
            var eventKey = $"{GeneratePartitionKeyPattern(partitionKeys)}:v{metadata.LatestVersion}";
            var envelope = await _daprClient.GetStateAsync<DaprEventEnvelope>(_stateStoreName, eventKey);
            
            if (envelope == null)
            {
                return ResultBox.FromValue<EventDocumentWithBlobData?>(null);
            }
            
            var @event = await _serialization.DeserializeEventAsync(envelope);
            if (@event == null)
            {
                return ResultBox.FromValue<EventDocumentWithBlobData?>(null);
            }
            
            var eventDocument = new EventDocument(
                @event,
                envelope.AggregateId,
                envelope.RootPartitionKey,
                envelope.Version);
            
            return ResultBox.FromValue<EventDocumentWithBlobData?>(new EventDocumentWithBlobData(eventDocument));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest event for partition keys {PartitionKeys}", partitionKeys);
            return ResultBox<EventDocumentWithBlobData?>.FromException(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteEventsByPartitionKeysAsync(PartitionKeys partitionKeys)
    {
        try
        {
            var keyPattern = GeneratePartitionKeyPattern(partitionKeys);
            
            // Get metadata to know how many events to delete
            var metadataKey = $"{keyPattern}:metadata";
            var metadata = await _daprClient.GetStateAsync<AggregateMetadata>(_stateStoreName, metadataKey);
            
            if (metadata != null)
            {
                // Delete all events
                for (var version = 1; version <= metadata.LatestVersion; version++)
                {
                    var eventKey = $"{keyPattern}:v{version}";
                    await _daprClient.DeleteStateAsync(_stateStoreName, eventKey);
                }
                
                // Delete metadata
                await _daprClient.DeleteStateAsync(_stateStoreName, metadataKey);
            }
            
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete events for partition keys {PartitionKeys}", partitionKeys);
            return ResultBox<bool>.FromException(ex);
        }
    }

    private string GenerateEventKey(EventDocument eventDocument)
    {
        var partitionKey = GeneratePartitionKeyPattern(eventDocument.PartitionKeys);
        return $"{partitionKey}:v{eventDocument.Version}";
    }

    private string GeneratePartitionKeyPattern(PartitionKeys partitionKeys)
    {
        return $"events:{partitionKeys.RootPartitionKey}:{partitionKeys.AggregateId}";
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