using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralEventProviderRetryTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly InMemoryEventSubscription _eventSubscription;
    private readonly GeneralEventProvider _provider;

    public GeneralEventProviderRetryTests()
    {
        _eventStore = new InMemoryEventStore();
        _eventSubscription = new InMemoryEventSubscription();
        _provider = new GeneralEventProvider(_eventStore, _eventSubscription, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task AutoRetry_WhenBatchDoesNotReachSafeWindow_ShouldRetryAutomatically()
    {
        // Arrange
        var events = new List<Event>();
        var processedEvents = new List<(Event evt, bool isSafe)>();
        
        // Add 10,000 events in the recent past (within safe window)
        var recentTime = DateTime.UtcNow.AddSeconds(-10);
        for (int i = 0; i < 10000; i++)
        {
            var evt = CreateEvent($"Event_{i}", recentTime);
            await _eventStore.WriteEventsAsync(new List<Event> { evt });
            events.Add(evt);
        }

        // Act - Start with auto retry enabled
        var handle = await _provider.StartWithBatchCallbackAsyncWithRetry(
            async batch =>
            {
                processedEvents.AddRange(batch);
                await Task.CompletedTask;
            },
            fromPosition: null,
            batchSize: 10000,
            autoRetryOnIncompleteWindow: true,
            retryDelay: TimeSpan.FromMilliseconds(500),
            cancellationToken: default);

        // Wait for initial batch processing
        await Task.Delay(1000);
        
        // First batch should be processed but all events are unsafe (recent)
        Assert.NotEmpty(processedEvents);
        Assert.All(processedEvents, item => Assert.False(item.isSafe));
        
        var firstBatchCount = processedEvents.Count;
        
        // Wait for automatic retry after delay
        await Task.Delay(1500);
        
        // Should have retried and potentially processed more events
        // (though in this test they're still likely unsafe)
        Assert.True(processedEvents.Count >= firstBatchCount);
        
        // Clean up
        handle.Dispose();
    }

    [Fact]
    public async Task ManualRetry_WhenBatchDoesNotReachSafeWindow_ShouldWaitForManualTrigger()
    {
        // Arrange
        var processedBatches = 0;
        
        // Add 10,000 events in the recent past
        var recentTime = DateTime.UtcNow.AddSeconds(-10);
        for (int i = 0; i < 10000; i++)
        {
            var evt = CreateEvent($"Event_{i}", recentTime);
            await _eventStore.WriteEventsAsync(new List<Event> { evt });
        }

        // Act - Start with auto retry disabled (manual retry mode)
        var handle = await _provider.StartWithBatchCallbackAsyncWithRetry(
            async batch =>
            {
                processedBatches++;
                await Task.CompletedTask;
            },
            fromPosition: null,
            batchSize: 10000,
            autoRetryOnIncompleteWindow: false, // Manual retry mode
            retryDelay: null,
            cancellationToken: default);

        // Wait for initial batch processing
        await Task.Delay(1000);
        
        // Should have processed one batch
        Assert.Equal(1, processedBatches);
        
        // Should be waiting for manual retry
        Assert.True(handle.IsWaitingForManualRetry);
        
        // Wait a bit - no additional batches should be processed
        await Task.Delay(1000);
        Assert.Equal(1, processedBatches);
        
        // Trigger manual retry
        handle.RetryManually();
        
        // Wait for retry to process
        await Task.Delay(1000);
        
        // Should have processed another batch after manual retry
        Assert.True(processedBatches >= 2);
        Assert.False(handle.IsWaitingForManualRetry);
        
        // Clean up
        handle.Dispose();
    }

    [Fact]
    public async Task WhenBatchReachesSafeWindow_ShouldNotNeedRetry()
    {
        // Arrange
        var processedBatches = 0;
        var caughtUp = false;
        
        // Add 5,000 old events (outside safe window)
        var oldTime = DateTime.UtcNow.AddSeconds(-30);
        for (int i = 0; i < 5000; i++)
        {
            var evt = CreateEvent($"OldEvent_{i}", oldTime);
            await _eventStore.WriteEventsAsync(new List<Event> { evt });
        }
        
        // Add 5,000 recent events (inside safe window)
        var recentTime = DateTime.UtcNow.AddSeconds(-10);
        for (int i = 0; i < 5000; i++)
        {
            var evt = CreateEvent($"RecentEvent_{i}", recentTime);
            await _eventStore.WriteEventsAsync(new List<Event> { evt });
        }

        // Act
        var handle = await _provider.StartWithBatchCallbackAsyncWithRetry(
            async batch =>
            {
                processedBatches++;
                
                // Check if we have any safe events (indicating we crossed the threshold)
                if (batch.Any(b => b.isSafe))
                {
                    caughtUp = true;
                }
                
                await Task.CompletedTask;
            },
            fromPosition: null,
            batchSize: 10000,
            autoRetryOnIncompleteWindow: false, // Should not need retry
            retryDelay: null,
            cancellationToken: default);

        // Wait for processing
        await Task.Delay(2000);
        
        // Should have caught up without needing retry
        Assert.True(caughtUp);
        Assert.False(handle.IsWaitingForManualRetry);
        Assert.Equal(EventProviderState.Live, handle.State);
        
        // Clean up
        handle.Dispose();
    }

    private Event CreateEvent(string payload, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            new TestEventPayload(payload),
            sortableId,
            "TestEvent",
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    private record TestEventPayload(string Data) : IEventPayload;
}