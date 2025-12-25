# Dapr PubSub Implementation for Sekiban

This document describes the PubSub implementation for Sekiban.Pure.Dapr, which enables real-time event propagation to
projectors using Dapr's pub/sub building block.

## Overview

The implementation follows the Orleans pattern where events are streamed to projectors in real-time, but uses Dapr
PubSub instead of Orleans streams. This provides:

- **Real-time event processing**: Events are immediately published when saved
- **Scalable projections**: Multiple projector instances can process events concurrently
- **Reliable delivery**: Dapr ensures at-least-once delivery semantics
- **Decoupled architecture**: Event producers and consumers are loosely coupled

## Architecture

```
┌─────────────────────────┐
│  AggregateEventHandler  │
│        Actor            │
│  (Saves & Publishes)    │
└───────────┬─────────────┘
            │ PublishEventAsync
            ▼
┌─────────────────────────┐
│    Dapr PubSub          │
│  (sekiban-pubsub)       │
│   Topic: events.all     │
└───────────┬─────────────┘
            │ Subscription
            ▼
┌─────────────────────────┐
│  EventPubSubController  │
│  (HTTP Endpoint)        │
│  /pubsub/events         │
└───────────┬─────────────┘
            │ Forward to Actors
            ▼
┌─────────────────────────┐
│  MultiProjectorActor    │
│  (Processes Events)     │
│  - Updates projections  │
│  - Maintains state      │
└─────────────────────────┘
```

## Implementation Details

### 1. Event Publishing (AggregateEventHandlerActor)

When events are saved, they are automatically published to Dapr PubSub:

```csharp
private async Task PublishEventsToPubSub(List<SerializableEventDocument> eventDocuments)
{
    foreach (var eventDoc in eventDocuments)
    {
        var envelope = new DaprEventEnvelope
        {
            EventId = eventDoc.Id,
            EventData = eventDoc.CompressedPayloadJson,
            EventType = eventDoc.PayloadTypeName,
            AggregateId = eventDoc.AggregateId,
            Version = eventDoc.Version,
            Timestamp = eventDoc.TimeStamp,
            SortableUniqueId = eventDoc.SortableUniqueId,
            RootPartitionKey = eventDoc.RootPartitionKey,
            IsCompressed = true,
            Metadata = new Dictionary<string, string>
            {
                ["PartitionGroup"] = eventDoc.AggregateGroup,
                ["ActorId"] = Id.GetId()
            }
        };
        
        await _daprClient.PublishEventAsync(PubSubName, EventTopicName, envelope);
    }
}
```

### 2. Event Subscription (EventPubSubController)

The controller receives events from PubSub and forwards them to projector actors:

```csharp
[Topic("sekiban-pubsub", "events.all")]
[HttpPost("events")]
public async Task<IActionResult> HandleEvent([FromBody] DaprEventEnvelope envelope)
{
    var projectorNames = _domainTypes.MultiProjectorsType.GetAllProjectorNames();
    
    var tasks = projectorNames.Select(async projectorName =>
    {
        var actorId = new ActorId(projectorName);
        var actor = _actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
            actorId, 
            nameof(MultiProjectorActor));
        
        await actor.HandlePublishedEvent(envelope);
    });

    await Task.WhenAll(tasks);
    return Ok();
}
```

### 3. Event Processing (MultiProjectorActor)

The projector actor processes events received via PubSub:

```csharp
public async Task HandlePublishedEvent(DaprEventEnvelope envelope)
{
    // Ensure state is loaded
    await EnsureStateLoadedAsync();

    // Deserialize the event
    var @event = await _serialization.DeserializeEventAsync(envelope);
    
    // Check for duplicates
    if (await IsSortableUniqueIdReceived(@event.SortableUniqueId))
    {
        return; // Already processed
    }
    
    // Add to buffer and flush
    _buffer.Add(@event);
    FlushBuffer();
    _pendingSave = true;
}
```

## Configuration

### Dapr Components

1. **PubSub Component** (`pubsub.yaml`):

```yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: sekiban-pubsub
spec:
  type: pubsub.in-memory  # Can be Redis, Azure Service Bus, etc.
  version: v1
```

2. **Subscription** (`subscription.yaml`):

```yaml
apiVersion: dapr.io/v2alpha1
kind: Subscription
metadata:
  name: domain-events-subscription
spec:
  topic: events.all
  routes:
    default: /pubsub/events
  pubsubname: sekiban-pubsub
scopes:
- dapr-sample-api
```

### Application Configuration

In `Program.cs`:

```csharp
builder.Services.AddSekibanWithDapr(domainTypes, options =>
{
    options.StateStoreName = "sekiban-eventstore";
    options.PubSubName = "sekiban-pubsub";
    options.EventTopicName = "events.all";
    options.ActorIdPrefix = "dapr-sample";
});
```

## Key Features

### 1. Duplicate Event Detection

Events are checked for duplicates using their `SortableUniqueId` to ensure idempotent processing.

### 2. State Management

- **Safe State**: Events older than 7 seconds, persisted every 5 minutes
- **Unsafe State**: Recent events, kept in memory for immediate queries

### 3. Real-time Updates

Events are processed immediately upon receipt, enabling real-time projection updates.

### 4. Fault Tolerance

- Events continue to be published even if one fails
- Actors maintain their state across restarts
- Dapr ensures reliable message delivery

## Testing

Use the provided test script to verify the implementation:

```bash
cd internalUsages/DaprSample
./test-pubsub.sh
```

The test script will:

1. Create test users
2. Update user information
3. Verify projections are updated in real-time
4. Stress test with multiple rapid updates

## Performance Considerations

1. **Event Batching**: Events are published individually to ensure real-time processing
2. **Compression**: Event payloads are compressed to reduce network overhead
3. **Buffering**: Events are buffered in the projector to handle bursts
4. **Snapshot Persistence**: State is persisted periodically to reduce recovery time

## Migration from Polling

The previous implementation used polling with reminders to check for new events. The PubSub implementation provides:

- **Lower latency**: Events are processed immediately instead of waiting for the next poll
- **Better resource usage**: No constant polling overhead
- **Improved scalability**: Multiple projectors can process events in parallel

## Troubleshooting

### Events Not Being Received

1. Check Dapr sidecar is running: `dapr list`
2. Verify PubSub component is configured correctly
3. Check subscription is registered: `curl http://localhost:3500/v1.0/metadata`
4. Review logs for errors in event publishing or processing

### Duplicate Events

The system handles duplicates automatically using `SortableUniqueId`. If you see duplicate processing:

1. Check the duplicate detection logic in `HandlePublishedEvent`
2. Verify `SortableUniqueId` is being generated correctly

### Performance Issues

1. Monitor event publishing rate
2. Check projector actor memory usage
3. Consider adjusting buffer flush frequency
4. Review state persistence interval

## Future Enhancements

1. **Event Filtering**: Allow projectors to subscribe to specific event types
2. **Parallel Processing**: Process independent events in parallel within a projector
3. **Metrics**: Add OpenTelemetry metrics for monitoring
4. **Dead Letter Queue**: Handle failed events with retry logic