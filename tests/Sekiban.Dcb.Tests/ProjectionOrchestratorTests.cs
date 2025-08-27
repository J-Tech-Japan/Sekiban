using Dcb.Domain;
using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.Diagnostics;
namespace Sekiban.Dcb.Tests;

public class ProjectionOrchestratorTests
{
    private static DcbDomainTypes CreateTestDomainTypes() => DomainType.GetDomainTypes();

    private static Event CreateTestEvent(DateTime? timestamp = null, string? data = null)
    {
        var eventId = Guid.NewGuid();
        var ts = timestamp ?? DateTime.UtcNow;
        
        return new Event(
            new WeatherForecastCreated(
                eventId,
                "Tokyo",
                DateOnly.FromDateTime(ts),
                25,
                "Sunny"),
            SortableUniqueId.Generate(ts, eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string> { nameof(WeatherForecastTag) });
    }

    private static List<Event> GenerateTestEvents(int count, DateTime? startTime = null)
    {
        var events = new List<Event>();
        var baseTime = startTime ?? DateTime.UtcNow.AddHours(-1);
        
        for (var i = 0; i < count; i++)
        {
            events.Add(CreateTestEvent(baseTime.AddSeconds(i)));
        }
        
        return events;
    }

    [Fact]
    public async Task Initialize_WithEmptyState_CreatesNewProjection()
    {
        // Arrange
        var domainTypes = CreateTestDomainTypes();
        var eventStore = new InMemoryEventStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            domainTypes,
            "WeatherForecastProjection",
            eventStore);

        // Act
        var result = await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Assert
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal("WeatherForecastProjection", state.ProjectorName);
        Assert.NotNull(state.Payload);
        Assert.Equal(0, state.Position.EventsProcessed);
    }

    [Fact]
    public async Task ProcessEvents_WithValidEvents_UpdatesProjection()
    {
        // Arrange
        var domainTypes = CreateTestDomainTypes();
        var eventStore = new InMemoryEventStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            domainTypes,
            "WeatherForecastProjection",
            eventStore);

        await orchestrator.InitializeAsync("WeatherForecastProjection");
        
        var events = GenerateTestEvents(10);
        var context = new ProcessingContext(
            IsStreaming: false,
            CheckDuplicates: true,
            BatchSize: 10,
            SafeWindow: TimeSpan.FromSeconds(20));

        // Act
        var result = await orchestrator.ProcessEventsAsync(events, context);

        // Assert
        Assert.True(result.IsSuccess);
        var processResult = result.GetValue();
        Assert.Equal(10, processResult.ProcessedCount);
        Assert.NotNull(processResult.LastPosition);
        
        // Verify projection state was updated
        var stateResult = await orchestrator.GetCurrentStateAsync();
        Assert.True(stateResult.IsSuccess);
        Assert.Equal(10, stateResult.GetValue().Position.EventsProcessed);
    }

    [Fact]
    public async Task ProcessEvents_WithDuplicates_FiltersDuplicates()
    {
        // Arrange
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection");

        await orchestrator.InitializeAsync("WeatherForecastProjection");
        
        var event1 = CreateTestEvent();
        var events = new List<Event> { event1, event1, event1 }; // Same event 3 times
        
        var context = new ProcessingContext(
            IsStreaming: false,
            CheckDuplicates: true,
            BatchSize: 10,
            SafeWindow: TimeSpan.FromSeconds(20));

        // Act
        var result = await orchestrator.ProcessEventsAsync(events, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.GetValue().ProcessedCount); // Only 1 unique event
    }

    [Fact]
    public async Task SimulateStreamEvent_ProcessesSingleEvent()
    {
        // Arrange
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection");

        await orchestrator.InitializeAsync("WeatherForecastProjection");
        var evt = CreateTestEvent();

        // Act
        var result = await orchestrator.SimulateStreamEventAsync(evt);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.GetValue().ProcessedCount);
        
        // Verify event was added to stream buffer
        var buffer = orchestrator.GetStreamBuffer();
        Assert.Single(buffer);
        Assert.Equal(evt.Id, buffer[0].Id);
    }

    [Fact]
    public async Task PersistAndRestore_MaintainsState()
    {
        // Arrange
        var domainTypes = CreateTestDomainTypes();
        var persistenceStore = new Sekiban.Dcb.InMemory.InMemoryPersistenceStore();
        var orchestrator1 = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            domainTypes,
            "WeatherForecastProjection",
            persistenceStore: persistenceStore);

        await orchestrator1.InitializeAsync("WeatherForecastProjection");
        
        // Process some events
        var events = GenerateTestEvents(5);
        var context = new ProcessingContext(false, true, 10, TimeSpan.FromSeconds(20));
        await orchestrator1.ProcessEventsAsync(events, context);

        // Act - Persist
        var persistResult = await orchestrator1.PersistAsync();
        Assert.True(persistResult.IsSuccess);

        // Create new orchestrator and restore
        var orchestrator2 = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            domainTypes,
            "WeatherForecastProjection",
            persistenceStore: persistenceStore);

        var loadResult = await orchestrator2.LoadPersistedStateAsync();
        Assert.True(loadResult.IsSuccess);

        // Assert - State was restored
        var restoredState = await orchestrator2.GetCurrentStateAsync();
        Assert.True(restoredState.IsSuccess);
        Assert.Equal(5, restoredState.GetValue().Position.EventsProcessed);
    }

    [Fact]
    public async Task SimulateCatchUp_ProcessesInBatches()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection",
            eventStore);

        // Add events to store
        var events = GenerateTestEvents(250);
        foreach (var evt in events)
        {
            await eventStore.AppendEventAsync(evt);
        }

        await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Act - Catch up with batch size 50
        var catchUpResult = await orchestrator.SimulateCatchUpAsync(batchSize: 50);

        // Assert
        Assert.True(catchUpResult.IsSuccess);
        var result = catchUpResult.GetValue();
        Assert.Equal(250, result.TotalProcessed);
        Assert.Equal(5, result.Batches); // 250 / 50 = 5 batches
        Assert.NotNull(result.LastPosition);
    }

    [Fact]
    public async Task LargeScaleProjection_PerformanceTest()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection",
            eventStore);

        // Generate large number of events
        var events = GenerateTestEvents(10000);
        foreach (var evt in events)
        {
            await eventStore.AppendEventAsync(evt);
        }

        await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Act
        var stopwatch = Stopwatch.StartNew();
        var catchUpResult = await orchestrator.SimulateCatchUpAsync(batchSize: 1000);
        stopwatch.Stop();

        // Assert
        Assert.True(catchUpResult.IsSuccess);
        Assert.Equal(10000, catchUpResult.GetValue().TotalProcessed);
        Assert.True(stopwatch.ElapsedMilliseconds < 10000); // Should complete within 10 seconds
        
        // Verify final state
        var state = await orchestrator.GetCurrentStateAsync();
        Assert.True(state.IsSuccess);
        Assert.Equal(10000, state.GetValue().Position.EventsProcessed);
    }

    [Fact]
    public async Task SafeWindowPosition_TrackedCorrectly()
    {
        // Arrange
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection",
            safeWindowDuration: TimeSpan.FromMinutes(1));

        await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Create events: some safe (old), some unsafe (recent)
        var now = DateTime.UtcNow;
        var safeEvents = GenerateTestEvents(5, now.AddMinutes(-2)); // Safe
        var unsafeEvents = GenerateTestEvents(5, now); // Unsafe

        var allEvents = safeEvents.Concat(unsafeEvents).ToList();
        var context = new ProcessingContext(false, true, 20, TimeSpan.FromMinutes(1));

        // Act
        var result = await orchestrator.ProcessEventsAsync(allEvents, context);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.GetValue().ProcessedCount);
        
        // Safe position should be from last safe event
        Assert.Equal(safeEvents.Last().SortableUniqueIdValue, result.GetValue().SafePosition);
        // Last position should be from last event overall
        Assert.Equal(allEvents.Last().SortableUniqueIdValue, result.GetValue().LastPosition);
    }

    [Fact]
    public async Task StreamAndCatchUp_MixedMode()
    {
        // Arrange
        var eventStore = new InMemoryEventStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection",
            eventStore);

        // Add historical events
        var historicalEvents = GenerateTestEvents(50);
        foreach (var evt in historicalEvents)
        {
            await eventStore.AppendEventAsync(evt);
        }

        await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Act
        // 1. Catch up from store
        var catchUpResult = await orchestrator.SimulateCatchUpAsync(batchSize: 25);
        Assert.True(catchUpResult.IsSuccess);
        Assert.Equal(50, catchUpResult.GetValue().TotalProcessed);

        // 2. Stream new events
        var streamEvents = GenerateTestEvents(10, DateTime.UtcNow);
        foreach (var evt in streamEvents)
        {
            var streamResult = await orchestrator.SimulateStreamEventAsync(evt);
            Assert.True(streamResult.IsSuccess);
        }

        // Assert - Total processed
        var finalState = await orchestrator.GetCurrentStateAsync();
        Assert.True(finalState.IsSuccess);
        Assert.Equal(60, finalState.GetValue().Position.EventsProcessed);
        
        // Verify stream buffer
        Assert.Equal(10, orchestrator.GetProcessedStreamEvents().Count);
    }

    [Fact]
    public async Task PersistenceStatistics_TrackedCorrectly()
    {
        // Arrange
        var persistenceStore = new Sekiban.Dcb.InMemory.InMemoryPersistenceStore();
        var orchestrator = new Sekiban.Dcb.InMemory.InMemoryProjectionOrchestrator(
            CreateTestDomainTypes(),
            "WeatherForecastProjection",
            persistenceStore: persistenceStore);

        await orchestrator.InitializeAsync("WeatherForecastProjection");

        // Act - Multiple persist operations
        for (var i = 0; i < 5; i++)
        {
            var events = GenerateTestEvents(10);
            await orchestrator.ProcessEventsAsync(
                events,
                new ProcessingContext(false, true, 10, TimeSpan.FromSeconds(20)));
            await orchestrator.PersistAsync();
        }

        // Assert
        var stats = await orchestrator.GetPersistenceStatisticsAsync();
        Assert.Equal(1, stats.TotalProjections);
        Assert.Equal(5, stats.TotalSaves);
        Assert.Contains("WeatherForecastProjection", stats.ProjectionSizes.Keys);
    }
}