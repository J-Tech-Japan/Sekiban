using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;

namespace SekibanDcbDecider.ApiService.Realtime;

public sealed class OrleansStreamEventRouter : IHostedService, IDisposable
{
    private static readonly HashSet<string> ReservationEventTypes = new(StringComparer.Ordinal)
    {
        "ReservationDraftCreated",
        "ReservationHoldCommitted",
        "ReservationConfirmed",
        "ReservationCancelled",
        "ReservationRejected",
        "ReservationExpiredCommitted",
        "ReservationDetailsUpdated"
    };

    private static readonly HashSet<string> ApprovalEventTypes = new(StringComparer.Ordinal)
    {
        "ApprovalFlowStarted",
        "ApprovalDecisionRecorded"
    };

    private readonly IClusterClient _clusterClient;
    private readonly IEventSubscriptionResolver _subscriptionResolver;
    private readonly SseTopicHub _hub;
    private readonly ILogger<OrleansStreamEventRouter> _logger;
    private IAsyncStream<SerializableEvent>? _stream;
    private StreamSubscriptionHandle<SerializableEvent>? _handle;

    public OrleansStreamEventRouter(
        IClusterClient clusterClient,
        IEventSubscriptionResolver subscriptionResolver,
        SseTopicHub hub,
        ILogger<OrleansStreamEventRouter> logger)
    {
        _clusterClient = clusterClient;
        _subscriptionResolver = subscriptionResolver;
        _hub = hub;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for Orleans silo to fully start and join the cluster
        // This is necessary because the silo needs time to register with the membership table
        const int maxRetries = 30;
        const int delayMs = 2000;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                await Task.Delay(delayMs, cancellationToken);
                // Try to resolve stream to verify Orleans is ready
                var testDescriptor = _subscriptionResolver.Resolve("ReservationProjector") as OrleansSekibanStream;
                if (testDescriptor != null)
                {
                    var testProvider = _clusterClient.GetStreamProvider(testDescriptor.ProviderName);
                    // If we can get the stream provider, Orleans is likely ready
                    break;
                }
            }
            catch (Exception ex) when (retry < maxRetries - 1)
            {
                _logger.LogWarning("Waiting for Orleans to be ready (attempt {Attempt}/{Max}): {Message}",
                    retry + 1, maxRetries, ex.Message);
            }
        }

        try
        {
            var streamDescriptor = _subscriptionResolver.Resolve("ReservationProjector") as OrleansSekibanStream;
            if (streamDescriptor == null)
            {
                _logger.LogError("Failed to resolve Orleans stream for realtime events.");
                return;
            }

            var provider = _clusterClient.GetStreamProvider(streamDescriptor.ProviderName);
            _stream = provider.GetStream<SerializableEvent>(StreamId.Create(streamDescriptor.StreamNamespace, streamDescriptor.StreamId));

            var observer = new StreamObserver(OnEventReceivedAsync, _logger);
            var existing = await _stream.GetAllSubscriptionHandles();
            if (existing != null && existing.Count > 0)
            {
                _handle = await existing[0].ResumeAsync(observer);
                for (int i = 1; i < existing.Count; i++)
                {
                    try
                    {
                        await existing[i].UnsubscribeAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to unsubscribe duplicate stream handle #{Index}", i);
                    }
                }
            }
            else
            {
                _handle = await _stream.SubscribeAsync(observer, null);
            }

            _logger.LogInformation("Realtime stream subscribed: {Provider}/{Namespace}/{StreamId}",
                streamDescriptor.ProviderName,
                streamDescriptor.StreamNamespace,
                streamDescriptor.StreamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start realtime stream subscription.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _handle?.UnsubscribeAsync().GetAwaiter().GetResult();
        _handle = null;
        _stream = null;
    }

    private Task OnEventReceivedAsync(SerializableEvent evt)
    {
        var eventType = evt.EventPayloadName;
        var update = new SseStreamUpdate(eventType, evt.SortableUniqueIdValue, evt.Id);

        if (ReservationEventTypes.Contains(eventType))
        {
            _hub.Publish(StreamTopics.Reservations, update);
        }

        if (ApprovalEventTypes.Contains(eventType))
        {
            _hub.Publish(StreamTopics.Approvals, update);
        }

        _logger.LogInformation("Realtime event published: {EventType} {SortableUniqueId}", eventType, evt.SortableUniqueIdValue);

        return Task.CompletedTask;
    }

    private sealed class StreamObserver : IAsyncObserver<SerializableEvent>
    {
        private readonly Func<SerializableEvent, Task> _onNext;
        private readonly ILogger _logger;

        public StreamObserver(Func<SerializableEvent, Task> onNext, ILogger logger)
        {
            _onNext = onNext;
            _logger = logger;
        }

        public Task OnNextAsync(SerializableEvent item, StreamSequenceToken? token = null) =>
            _onNext(item);

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex)
        {
            _logger.LogError(ex, "Realtime stream observer error");
            return Task.CompletedTask;
        }
    }
}

public static class StreamTopics
{
    public const string Reservations = "reservations";
    public const string Approvals = "approvals";
}
