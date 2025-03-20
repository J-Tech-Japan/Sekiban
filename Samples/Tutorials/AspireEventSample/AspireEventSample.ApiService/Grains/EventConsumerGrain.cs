using AspireEventSample.ApiService.Aggregates.ReadModel;
using Orleans.Streams;
using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Grains;

[ImplicitStreamSubscription("AllEvents")]
public class EventConsumerGrain : Grain, IEventConsumerGrain
{
    private readonly ILogger<EventConsumerGrain> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEventContextProvider _eventContextProvider;
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _subscriptionHandle;
    private EventProcessor? _eventProcessor;
    private readonly List<IReadModelHandler> _handlers = new();

    public EventConsumerGrain(
        ILogger<EventConsumerGrain> logger,
        ILoggerFactory loggerFactory,
        IEventContextProvider eventContextProvider)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _eventContextProvider = eventContextProvider;
    }

    public Task OnErrorAsync(Exception ex)
    {
        _logger.LogError(ex, "Error in event stream");
        return Task.CompletedTask;
    }

    public Task OnNextAsync(IEvent item, StreamSequenceToken? token)
    {
        _logger.LogDebug(
            "Processing event {EventType} with ID {EventId}",
            item.GetPayload().GetType().Name,
            item.PartitionKeys.AggregateId);

        return _eventProcessor!.ProcessEventAsync(item);
    }

    public Task OnCompletedAsync()
    {
        _logger.LogInformation("Event stream completed");
        return Task.CompletedTask;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activating EventConsumerGrain");

        // Get grain references
        var branchWriter = GrainFactory.GetGrain<IBranchEntityPostgresReadModelAccessorGrain>("default");
        var cartPostgresWriter = GrainFactory.GetGrain<ICartEntityPostgresWriter>("default");

        // Create handlers directly
        _handlers.Add(
            new BranchReadModelHandler(
                branchWriter,
                _eventContextProvider,
                _loggerFactory.CreateLogger<BranchReadModelHandler>()));

        // Get cart item writer grain
        var cartItemPostgresWriter = GrainFactory.GetGrain<ICartItemEntityPostgresWriterGrain>("default");

        _handlers.Add(
            new ShoppingCartReadModelHandler(
                cartPostgresWriter,
                cartItemPostgresWriter,
                _eventContextProvider,
                _loggerFactory.CreateLogger<ShoppingCartReadModelHandler>()));

        // Create event processor
        _eventProcessor = new EventProcessor(
            _handlers,
            _eventContextProvider,
            _loggerFactory.CreateLogger<EventProcessor>());

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