# Orleans Event Subscription

This folder contains the Orleans implementation of the `IEventSubscription` interface from Sekiban.Dcb, providing event streaming capabilities for both actors and external consumers.

## Features

- **Event Subscription**: Subscribe to event streams with customizable callbacks
- **Filtering**: Filter events by type, tags, or custom predicates
- **Resume Support**: Resume subscriptions from specific positions after failures
- **Flexible Usage**: Can be used inside Orleans grains or outside for read models, SignalR, etc.

## Components

### Core Classes

1. **OrleansEventSubscription**: Basic implementation of IEventSubscription using Orleans streams
2. **OrleansEventSubscriptionHandleSimple**: Simple handle for managing individual subscriptions
3. **DirectOrleansEventSubscription**: Implementation for grains that directly subscribe to Orleans streams
4. **EventFilters**: Common filter implementations


## Usage Examples

### Basic Subscription (Inside a Grain)

```csharp
public class MyGrain : Grain, IMyGrain
{
    private IEventSubscription _subscription;
    private IEventSubscriptionHandle _handle;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Create subscription
        _subscription = new OrleansEventSubscription(
            GrainFactory.GetClusterClient(),
            "EventStream",           // Provider name
            "Sekiban.Events",        // Namespace
            Guid.NewGuid()          // Stream ID
        );

        // Subscribe to all events
        _handle = await _subscription.SubscribeAsync(
            async (evt) => await ProcessEventAsync(evt),
            subscriptionId: $"grain-{this.GetPrimaryKey()}",
            cancellationToken
        );
    }

    private async Task ProcessEventAsync(Event evt)
    {
        // Process the event
        Console.WriteLine($"Received event: {evt.EventType}");
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        // Clean up subscription
        await _handle?.UnsubscribeAsync();
        _subscription?.Dispose();
    }
}
```

### Filtered Subscription (Outside Actor)

```csharp
public class ReadModelProjector
{
    private readonly IClusterClient _clusterClient;
    private readonly IEventSubscription _subscription;

    public ReadModelProjector(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
        
        // Create subscription
        _subscription = new OrleansEventSubscription(
            _clusterClient,
            "EventStream",
            "Sekiban.Events"
        );
    }

    public async Task StartAsync()
    {
        // Create a filter for specific event types
        var filter = new EventFilters.EventTypeFilter(
            "StudentCreated", 
            "StudentEnrolled", 
            "StudentDropped"
        );

        // Subscribe with filter
        var handle = await _subscription.SubscribeWithFilterAsync(
            filter,
            async (evt) => await UpdateReadModelAsync(evt)
        );
    }

    private async Task UpdateReadModelAsync(Event evt)
    {
        // Update read model based on event
        switch (evt.EventType)
        {
            case "StudentCreated":
                // Handle student creation
                break;
            case "StudentEnrolled":
                // Handle enrollment
                break;
            case "StudentDropped":
                // Handle drop
                break;
        }
    }
}
```

### Direct Subscription in Grain

```csharp
public class ProjectionGrain : Grain, IProjectionGrain
{
    private StreamSubscriptionHandle<Event> _orleansStreamHandle;
    private DirectOrleansEventSubscription _subscription;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Subscribe directly to Orleans stream
        var streamProvider = this.GetStreamProvider("EventStreamProvider");
        var stream = streamProvider.GetStream<Event>(StreamId.Create("AllEvents", Guid.Empty));
        
        // Create observer for the stream
        var observer = new EventObserver(this);
        _orleansStreamHandle = await stream.SubscribeAsync(observer, token: null);
        
        // Create DirectOrleansEventSubscription with existing handle
        _subscription = new DirectOrleansEventSubscription(stream, _orleansStreamHandle);
        
        await base.OnActivateAsync(cancellationToken);
    }

    private class EventObserver : IAsyncObserver<Event>
    {
        private readonly ProjectionGrain _grain;
        
        public EventObserver(ProjectionGrain grain)
        {
            _grain = grain;
        }
        
        public async Task OnNextAsync(Event item, StreamSequenceToken token = null)
        {
            await _grain.ProcessEventAsync(item);
        }
        
        public Task OnCompletedAsync() => Task.CompletedTask;
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
    }

    private async Task ProcessEventAsync(Event evt)
    {
        // Your event processing logic here
        Console.WriteLine($"Processing: {evt.EventType} at position {evt.SortableUniqueIdValue}");
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_orleansStreamHandle != null)
        {
            await _orleansStreamHandle.UnsubscribeAsync();
        }
        await base.OnDeactivateAsync(reason, cancellationToken);
    }
}
```

### Complex Filtering

```csharp
public async Task SubscribeWithComplexFilterAsync()
{
    // Create composite filter
    var typeFilter = new EventFilters.EventTypeFilter("OrderCreated", "OrderShipped");
    var tagFilter = new EventFilters.TagGroupFilter("Order", "Customer");
    
    // Combine with AND logic
    var combinedFilter = new EventFilters.CompositeAndFilter(typeFilter, tagFilter);
    
    // Or use a custom predicate
    var customFilter = new EventFilters.PredicateFilter(evt =>
        evt.EventMetadata.CreatedBy == "OrderService" &&
        evt.Tags.Any(t => t.StartsWith("Priority:High"))
    );

    // Subscribe with filter
    var handle = await subscription.SubscribeWithFilterAsync(
        customFilter,
        async (evt) => await ProcessHighPriorityOrderAsync(evt)
    );
}
```

### SignalR Integration Example

```csharp
public class EventHub : Hub
{
    private readonly IEventSubscription _subscription;
    private IEventSubscriptionHandle _handle;

    public EventHub(IClusterClient clusterClient)
    {
        _subscription = new OrleansEventSubscription(
            clusterClient,
            "EventStream",
            "Sekiban.Events"
        );
    }

    public async Task SubscribeToEvents(string[] eventTypes)
    {
        // Create filter for requested event types
        var filter = new EventFilters.EventTypeFilter(eventTypes);
        
        // Subscribe and forward to SignalR clients
        _handle = await _subscription.SubscribeWithFilterAsync(
            filter,
            async (evt) =>
            {
                await Clients.Caller.SendAsync("EventReceived", new
                {
                    Type = evt.EventType,
                    Id = evt.Id,
                    Timestamp = evt.EventMetadata.CreatedAt,
                    Payload = evt.Payload
                });
            },
            subscriptionId: Context.ConnectionId
        );
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        if (_handle != null)
        {
            await _handle.UnsubscribeAsync();
        }
        await base.OnDisconnectedAsync(exception);
    }
}
```

## Configuration

### Stream Provider Configuration

Configure Orleans stream providers in your silo configuration:

```csharp
siloBuilder.AddMemoryGrainStorage("PubSubStore")
    .AddSimpleMessageStreamProvider("EventStream");
```

Or for production:

```csharp
siloBuilder.AddEventHubStreams("EventStream", configurator =>
{
    configurator.ConfigureEventHub(builder =>
    {
        builder.Configure(options =>
        {
            options.ConnectionString = "your-event-hub-connection-string";
            options.ConsumerGroup = "sekiban-consumers";
            options.Path = "events";
        });
    });
    configurator.UseEventHubCheckpointer(builder =>
    {
        builder.Configure(options =>
        {
            options.ConnectionString = "your-storage-connection-string";
            options.ContainerName = "checkpoints";
        });
    });
});
```

## Best Practices

1. **Subscription Lifecycle**: Always dispose subscriptions properly to avoid memory leaks
2. **Error Handling**: Implement proper error handling in event callbacks
3. **Checkpointing**: Use checkpoints for critical processing that must not lose events
4. **Filtering**: Apply filters at subscription time to reduce network traffic
5. **Batching**: Consider batching event processing for better performance
6. **Idempotency**: Ensure event handlers are idempotent for at-least-once delivery

## Testing

For unit testing, use the `InMemoryCheckpointManager`:

```csharp
[Fact]
public async Task Should_Resume_From_Checkpoint()
{
    var checkpointManager = new InMemoryCheckpointManager();
    
    // Save a checkpoint
    await checkpointManager.SaveCheckpointAsync("test-sub", "position-123");
    
    // Create subscription
    var subscription = new OrleansEventSubscriptionWithCheckpoint(
        _clusterClient,
        checkpointManager,
        "EventStream",
        "Test.Events"
    );
    
    // Will resume from position-123
    var handle = await subscription.SubscribeAsync(
        async (evt) => { /* process */ },
        subscriptionId: "test-sub"
    );
}
```

## Monitoring and Status

The enhanced implementation provides detailed status information about subscriptions:

```csharp
public class SubscriptionMonitor
{
    private readonly IEventSubscription _subscription;
    
    public SubscriptionMonitor(IEventSubscription subscription)
    {
        _subscription = subscription;
    }
    
    public void PrintStatus()
    {
        // Get all subscription statuses
        var statuses = _subscription.GetAllSubscriptionStatuses();
        
        foreach (var status in statuses)
        {
            Console.WriteLine($"Subscription: {status.SubscriptionId}");
            Console.WriteLine($"  Active: {status.IsActive}");
            Console.WriteLine($"  Paused: {status.IsPaused}");
            Console.WriteLine($"  Started: {status.StartedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"  Position: {status.CurrentPosition}");
            Console.WriteLine($"  Events Received: {status.EventsReceived}");
            Console.WriteLine($"  Events Processed: {status.EventsProcessed}");
            Console.WriteLine($"  Errors: {status.ErrorCount}");
            Console.WriteLine($"  Avg Processing Time: {status.AverageProcessingTimeMs:F2}ms");
            
            if (status.LastEventProcessedAt.HasValue)
            {
                var timeSinceLastEvent = DateTime.UtcNow - status.LastEventProcessedAt.Value;
                Console.WriteLine($"  Time since last event: {timeSinceLastEvent.TotalSeconds:F1}s");
            }
            
            if (!string.IsNullOrEmpty(status.LastError))
            {
                Console.WriteLine($"  Last Error: {status.LastError} at {status.LastErrorAt}");
            }
        }
    }
    
    public async Task<HealthStatus> CheckHealthAsync(string subscriptionId)
    {
        var status = _subscription.GetSubscriptionStatus(subscriptionId);
        
        if (status == null)
            return HealthStatus.NotFound;
        
        if (!status.IsActive)
            return HealthStatus.Stopped;
        
        if (status.IsPaused)
            return HealthStatus.Paused;
        
        // Check if subscription is stale (no events for > 5 minutes)
        if (status.LastEventReceivedAt.HasValue)
        {
            var timeSinceLastEvent = DateTime.UtcNow - status.LastEventReceivedAt.Value;
            if (timeSinceLastEvent > TimeSpan.FromMinutes(5))
            {
                return HealthStatus.Stale;
            }
        }
        
        // Check error rate
        if (status.EventsProcessed > 0)
        {
            var errorRate = (double)status.ErrorCount / status.EventsProcessed;
            if (errorRate > 0.1) // More than 10% errors
            {
                return HealthStatus.Degraded;
            }
        }
        
        return HealthStatus.Healthy;
    }
}

public enum HealthStatus
{
    Healthy,
    Degraded,
    Stale,
    Paused,
    Stopped,
    NotFound
}
```

### Dashboard Integration Example

```csharp
// ASP.NET Core endpoint for subscription status
app.MapGet("/api/subscriptions/status", (IEventSubscription subscription) =>
{
    var statuses = subscription.GetAllSubscriptionStatuses();
    return Results.Ok(statuses.Select(s => new
    {
        s.SubscriptionId,
        s.IsActive,
        s.IsPaused,
        s.CurrentPosition,
        s.EventsReceived,
        s.EventsProcessed,
        s.ErrorCount,
        s.AverageProcessingTimeMs,
        LastActivity = s.LastEventProcessedAt,
        Health = GetHealthStatus(s)
    }));
});

// Real-time monitoring with SignalR
public class MonitoringHub : Hub
{
    private readonly IEventSubscription _subscription;
    private Timer? _timer;
    
    public MonitoringHub(IEventSubscription subscription)
    {
        _subscription = subscription;
    }
    
    public override async Task OnConnectedAsync()
    {
        // Send status updates every 5 seconds
        _timer = new Timer(async _ =>
        {
            var statuses = _subscription.GetAllSubscriptionStatuses();
            await Clients.Caller.SendAsync("SubscriptionStatusUpdate", statuses);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        
        await base.OnConnectedAsync();
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _timer?.Dispose();
        await base.OnDisconnectedAsync(exception);
    }
}
```

## Future Enhancements

- [ ] Implement persistent checkpoint managers (SQL, Azure Storage, etc.)
- [ ] Add metrics and monitoring with OpenTelemetry
- [ ] Support for event replay
- [ ] Batch processing support
- [ ] Dead letter queue handling
- [ ] Subscription groups for competing consumers
- [ ] Enhanced statistics with percentiles and histograms