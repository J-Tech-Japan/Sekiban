# Orleans Multi-Projection Grain

This folder contains the Orleans grain implementation for multi-projections in Sekiban.Dcb.

## Overview

The `MultiProjectionGrain` is an Orleans grain that:
- Manages multi-projection state using `GeneralMultiProjectionActor`
- Handles event streaming using `GeneralEventProvider`
- Provides automatic state persistence with size limit handling
- Supports lazy initialization on first access
- Offers monitoring and management capabilities

## Features

- **Lazy Initialization**: The grain prepares during activation but only starts event subscription on first access
- **State Persistence**: Automatic periodic persistence with configurable intervals
- **Size Limit Handling**: Gracefully handles state size limits (e.g., Cosmos DB's 2MB limit)
- **Event Ordering**: Maintains safe and unsafe states with proper event ordering
- **Monitoring**: Built-in status tracking and health monitoring
- **Resilience**: Automatic restart on failures with configurable retry logic

## Configuration

### Silo Configuration

```csharp
// In your silo configuration
siloBuilder
    .AddMultiProjectionGrain(options =>
    {
        options.MaxStateSize = 2 * 1024 * 1024; // 2MB
        options.PersistInterval = TimeSpan.FromMinutes(5);
        options.SafeWindowDuration = TimeSpan.FromSeconds(20);
        options.EventBatchSize = 1000;
        options.UseMemoryStorage = false; // Use true for development
    })
    .AddAzureTableGrainStorage("OrleansStorage", options =>
    {
        options.ConnectionString = "your-connection-string";
    });

// Or with Cosmos DB
siloBuilder
    .AddMultiProjectionGrain()
    .AddCosmosDBGrainStorage("OrleansStorage", options =>
    {
        options.AccountEndpoint = "https://your-account.documents.azure.com:443/";
        options.AccountKey = "your-key";
        options.DB = "SekibanDB";
        options.Container = "MultiProjections";
    });
```

### Client Configuration

```csharp
clientBuilder.AddMultiProjectionGrainClient();
```

### Service Registration

```csharp
// In your host configuration
services.AddSingleton<MultiProjectionGrainService>();

// Add hosted service for automatic management
services.AddHostedService<MultiProjectionGrainHostedService>(provider =>
{
    var client = provider.GetRequiredService<IClusterClient>();
    var service = provider.GetRequiredService<MultiProjectionGrainService>();
    
    // List of projector names to manage
    var projectorNames = new[] 
    { 
        "StudentEnrollmentProjector",
        "ClassRoomOccupancyProjector",
        "DailyStatisticsProjector"
    };
    
    return new MultiProjectionGrainHostedService(client, service, projectorNames);
});
```

## Usage Examples

### Basic Usage

```csharp
public class ProjectionController : ControllerBase
{
    private readonly IClusterClient _clusterClient;
    
    public ProjectionController(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient;
    }
    
    [HttpGet("projections/{projectorName}/state")]
    public async Task<IActionResult> GetProjectionState(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        var state = await grain.GetStateAsync();
        
        if (state.IsSuccess)
        {
            return Ok(state.GetValue());
        }
        
        return StatusCode(500, state.GetException().Message);
    }
    
    [HttpGet("projections/{projectorName}/status")]
    public async Task<IActionResult> GetProjectionStatus(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        var status = await grain.GetStatusAsync();
        
        return Ok(status);
    }
    
    [HttpPost("projections/{projectorName}/persist")]
    public async Task<IActionResult> PersistProjection(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        var result = await grain.PersistStateAsync();
        
        if (result.IsSuccess)
        {
            return Ok("State persisted successfully");
        }
        
        return StatusCode(500, result.GetException().Message);
    }
}
```

### Using the Service Helper

```csharp
public class ProjectionService
{
    private readonly MultiProjectionGrainService _grainService;
    
    public ProjectionService(MultiProjectionGrainService grainService)
    {
        _grainService = grainService;
    }
    
    public async Task<ProjectionSummary> GetProjectionSummaryAsync(string projectorName)
    {
        var status = await _grainService.GetProjectionStatusAsync(projectorName);
        var state = await _grainService.GetProjectionStateAsync(projectorName);
        
        return new ProjectionSummary
        {
            Name = projectorName,
            IsActive = status.IsSubscriptionActive,
            IsCaughtUp = status.IsCaughtUp,
            EventsProcessed = status.EventsProcessed,
            LastEventTime = status.LastEventTime,
            StateSize = status.StateSize,
            HasData = state.IsSuccess
        };
    }
    
    public async Task RestartProjectionAsync(string projectorName)
    {
        // Stop and restart the subscription
        await _grainService.StopProjectionSubscriptionAsync(projectorName);
        await Task.Delay(1000); // Brief pause
        await _grainService.StartProjectionSubscriptionAsync(projectorName);
    }
}
```

### Monitoring Dashboard

```csharp
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly MultiProjectionGrainService _service;
    private readonly string[] _projectorNames;
    
    public DashboardController(MultiProjectionGrainService service)
    {
        _service = service;
        _projectorNames = new[]
        {
            "StudentEnrollmentProjector",
            "ClassRoomOccupancyProjector",
            "DailyStatisticsProjector"
        };
    }
    
    [HttpGet("projections")]
    public async Task<IActionResult> GetAllProjections()
    {
        var statuses = await _service.GetAllProjectionStatusesAsync(_projectorNames);
        
        var dashboard = statuses.Select(kvp => new
        {
            Name = kvp.Key,
            Status = kvp.Value.IsSubscriptionActive ? "Active" : "Inactive",
            Health = GetHealth(kvp.Value),
            kvp.Value.IsCaughtUp,
            kvp.Value.EventsProcessed,
            kvp.Value.LastEventTime,
            StateSizeMB = kvp.Value.StateSize / (1024.0 * 1024.0),
            kvp.Value.LastError
        });
        
        return Ok(dashboard);
    }
    
    private string GetHealth(MultiProjectionGrainStatus status)
    {
        if (status.HasError) return "Error";
        if (!status.IsSubscriptionActive) return "Stopped";
        if (!status.IsCaughtUp) return "Catching Up";
        
        // Check if stale (no events for > 5 minutes)
        if (status.LastEventTime.HasValue)
        {
            var timeSinceLastEvent = DateTime.UtcNow - status.LastEventTime.Value;
            if (timeSinceLastEvent > TimeSpan.FromMinutes(5))
            {
                return "Stale";
            }
        }
        
        return "Healthy";
    }
}
```

### Testing

```csharp
[Fact]
public async Task MultiProjectionGrain_Should_Process_Events()
{
    // Arrange
    var cluster = new TestClusterBuilder()
        .AddSiloBuilderConfigurator<TestSiloConfigurator>()
        .Build();
    
    await cluster.DeployAsync();
    var client = cluster.Client;
    
    var grain = client.GetMultiProjectionGrain("TestProjector");
    
    // Act - Add some test events
    var events = new List<Event>
    {
        new Event(
            new StudentCreated(Guid.NewGuid(), "John Doe"),
            SortableUniqueId.GenerateNew().Value,
            "StudentCreated",
            Guid.NewGuid(),
            new EventMetadata("1", "Test", "Unit Test"),
            new List<string> { "Student:123" })
    };
    
    await grain.AddEventsAsync(events, finishedCatchUp: true);
    
    // Assert
    var status = await grain.GetStatusAsync();
    Assert.Equal(1, status.EventsProcessed);
    Assert.True(status.IsCaughtUp);
    
    var state = await grain.GetStateAsync();
    Assert.True(state.IsSuccess);
    
    // Cleanup
    await cluster.DisposeAsync();
}

public class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMultiProjectionGrain(options =>
            {
                options.UseMemoryStorage = true;
                options.PersistInterval = TimeSpan.FromSeconds(10);
            })
            .ConfigureServices(services =>
            {
                // Add test services
                services.AddSingleton<DcbDomainTypes>(DomainType.GetDomainTypes());
                services.AddSingleton<IEventStore, InMemoryEventStore>();
                services.AddSingleton<IEventSubscription, InMemoryEventSubscription>();
            });
    }
}
```

## State Persistence

The grain handles state persistence with the following considerations:

1. **Automatic Persistence**: State is automatically persisted at configurable intervals (default: 5 minutes)
2. **Size Limits**: If state exceeds the configured size limit (e.g., 2MB for Cosmos DB), the persistence is skipped with a warning
3. **Safe State Only**: Only safe state (events outside the safe window) is persisted to ensure consistency
4. **Graceful Degradation**: If persistence fails, the grain continues operating and logs the error

### Handling Large States

When dealing with large projections that might exceed storage limits:

```csharp
// Configure with appropriate limits
siloBuilder.AddMultiProjectionGrain(options =>
{
    // Set based on your storage provider limits
    options.MaxStateSize = 400 * 1024; // 400KB for Cosmos DB with overhead
    options.PersistInterval = TimeSpan.FromMinutes(2); // More frequent for smaller states
});

// Monitor state size
var status = await grain.GetStatusAsync();
if (status.StateSize > 300 * 1024) // Warning threshold
{
    _logger.LogWarning($"Projection {status.ProjectorName} state size is {status.StateSize} bytes");
}
```

## Performance Considerations

1. **Grain Activation**: Grains are activated on demand and deactivated after idle timeout
2. **Event Batching**: Events are processed in configurable batches (default: 1000)
3. **Memory Usage**: Each grain maintains in-memory state; consider memory limits
4. **Persistence Overhead**: Adjust persistence interval based on state size and change frequency

## Troubleshooting

### Common Issues

1. **State Too Large**: Check logs for "State size exceeds limit" messages
   - Solution: Reduce projection complexity or increase MaxStateSize

2. **Subscription Not Starting**: Check grain status for errors
   - Solution: Manually restart subscription or check event store connectivity

3. **Events Not Processing**: Verify IsCaughtUp status
   - Solution: Wait for catch-up to complete or check for event provider errors

4. **Persistence Failures**: Check storage provider connectivity
   - Solution: Verify connection strings and permissions

### Monitoring Endpoints

```csharp
// Health check endpoint
app.MapGet("/health/projections", async (MultiProjectionGrainService service) =>
{
    var projectorNames = new[] { "Projector1", "Projector2" };
    var statuses = await service.GetAllProjectionStatusesAsync(projectorNames);
    
    var allHealthy = statuses.Values.All(s => 
        s.IsSubscriptionActive && !s.HasError);
    
    return allHealthy 
        ? Results.Ok("All projections healthy") 
        : Results.Problem("Some projections unhealthy");
});
```

## Future Enhancements

- [ ] Support for projection versioning and migrations
- [ ] Automatic state compaction for large projections
- [ ] Support for projection snapshots to external storage
- [ ] Metrics and telemetry integration
- [ ] Support for projection replays from specific points
- [ ] Clustering of related projections