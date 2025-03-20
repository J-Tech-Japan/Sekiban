using AspireEventSample.ApiService.Aggregates.ReadModel;
using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Grains;

[ImplicitStreamSubscription("AllEvents")]
public class EventConsumerGrain : Grain, IEventConsumerGrain
{
    private readonly OrleansStreamEventSourceAdapter _adapter;
    private readonly ILogger<EventConsumerGrain> _logger;
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _subscriptionHandle;
    
    public EventConsumerGrain(
        OrleansStreamEventSourceAdapter adapter,
        ILogger<EventConsumerGrain> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }
    
    public Task OnErrorAsync(Exception ex)
    {
        _logger.LogError(ex, "Error in event stream");
        return Task.CompletedTask;
    }
    
    // Use adapter to process events
    public Task OnNextAsync(IEvent item, StreamSequenceToken? token)
    {
        _logger.LogDebug("Processing event {EventType} with ID {EventId}",
            item.GetPayload().GetType().Name, item.PartitionKeys.AggregateId);
            
        return _adapter.ProcessStreamEventAsync(item, token);
    }
    
    public Task OnCompletedAsync()
    {
        _logger.LogInformation("Event stream completed");
        return Task.CompletedTask;
    }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activating EventConsumerGrain");

        var streamProvider = this.GetStreamProvider("EventStreamProvider");

        _stream = streamProvider.GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));

        // Subscribe to the stream when this grain is activated
        _subscriptionHandle = await _stream.SubscribeAsync(
            (evt, token) => OnNextAsync(evt, token), // When an event is received
            OnErrorAsync, // When an error occurs
            OnCompletedAsync // When the stream completes
        );

        await base.OnActivateAsync(cancellationToken);
    }
}
