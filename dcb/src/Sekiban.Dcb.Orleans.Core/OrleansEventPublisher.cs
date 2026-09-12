using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Orleans.Serialization;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Tags;
using System.Threading.Channels;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Publishes Sekiban Dcb events to Orleans streams using an Orleans cluster client.
/// </summary>
public class OrleansEventPublisher : IEventPublisher, IExecutorSizePreparedDestinationPublisher
{
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansEventPublisher> _logger;
    private readonly IStreamDestinationResolver _resolver;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly DeepCopier? _deepCopier;
    private readonly Channel<PublishItem> _channel;
    private readonly Task _processor;

    /// <summary>
    /// Test-only observation of the planned enqueue boundary. This stays internal so production callers cannot
    /// replace the publisher's retry or context behavior; Orleans tests use it to assert plan identity.
    /// </summary>
    internal Action<
        SerializableEvent,
        string,
        string,
        Guid,
        IReadOnlyDictionary<string, object>?>? PlannedPublishObserver { get; set; }

    private record PublishItem(
        string Provider,
        string Namespace,
        Guid StreamId,
        SerializableEvent Event,
        int Attempt,
        Dictionary<string, object>? RequestContext = null);

    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger)
        : this(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            new DefaultServiceIdProvider())
    {
    }

    /// <summary>Additive constructor retaining the resolver/service identity used by publication.</summary>
    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _clusterClient = clusterClient;
        _resolver = resolver;
        _domainTypes = domainTypes;
        _logger = logger;
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _deepCopier = _clusterClient.ServiceProvider.GetService<DeepCopier>();
        // Unbounded channel for simplicity; projection side is idempotent
        _channel = Channel.CreateUnbounded<PublishItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _processor = Task.Run(ProcessQueueAsync);
    }

    public async Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        // Reduce noise: only trace this queueing operation
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogTrace("Queueing {EventCount} events to Orleans streams (at-least-once)", events.Count);
        }

        foreach (var (evt, tags) in events)
        {
            try
            {
                // Convert Event to SerializableEvent for Orleans stream
                var serializableEvent = evt.ToSerializableEvent(_domainTypes.EventTypes);

                var streams = _resolver.Resolve(evt, tags);
                foreach (var s in streams)
                {
                    if (s is OrleansSekibanStream os)
                    {
                        var item = new PublishItem(os.ProviderName, os.StreamNamespace, os.StreamId, serializableEvent, 0);
                        _channel.Writer.TryWrite(item);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to resolve streams for event {EventType} (ID: {EventId})",
                    evt.EventType, evt.Id);
            }
        }

        // Return immediately; background processor ensures at-least-once with retry and logging
        await Task.CompletedTask;
    }

    public ExecutorSizeDestinationPlan? CaptureDestinationPlan(
        Event @event,
        IReadOnlyCollection<ITag> tags,
        string serviceId)
    {
        var serializableEvent = @event.ToSerializableEvent(_domainTypes.EventTypes);
        return CaptureDestinationPlanCore(@event, serializableEvent, tags, serviceId);
    }

    ExecutorSizeDestinationPlan? IExecutorSizePreparedDestinationPublisher.CaptureDestinationPlan(
        Event @event,
        SerializableEvent serializedEvent,
        IReadOnlyCollection<ITag> tags,
        string serviceId)
        => CaptureDestinationPlanCore(@event, serializedEvent, tags, serviceId);

    private ExecutorSizeDestinationPlan? CaptureDestinationPlanCore(
        Event @event,
        SerializableEvent serializedEvent,
        IReadOnlyCollection<ITag> tags,
        string serviceId)
    {
        var destinations = (_resolver.Resolve(@event, tags) ?? Enumerable.Empty<ISekibanStream>())
            .OfType<OrleansSekibanStream>()
            .Select(stream => new OrleansDestination(
                stream.ProviderName,
                stream.StreamNamespace,
                stream.StreamId))
            .ToArray();

        if (destinations.Length == 0)
        {
            return null;
        }

        var captures = _clusterClient.ServiceProvider
            .GetServices<IOrleansDestinationMeasurementCapture>()
            .ToArray();
        var destinationStates = new List<OrleansDestinationPlanState>(destinations.Length);
        foreach (var destination in destinations)
        {
            string? failureReason = null;
            Dictionary<string, object>? requestContext = null;
            if (_deepCopier is null)
            {
                failureReason = "Orleans request-context deep-copy capability is unavailable";
            }
            else
            {
                try
                {
                    requestContext = RequestContextExtensions.Export(_deepCopier);
                }
                catch (Exception ex)
                {
                    failureReason = $"Orleans request-context capture failed with {ex.GetType().Name}";
                }
            }

            var capture = captures.FirstOrDefault(candidate => candidate.Matches(destination.ProviderName));
            object? providerState = null;
            if (capture is null)
            {
                failureReason ??= $"no destination measurement capability is registered for provider '{destination.ProviderName}'";
            }
            else
            {
                providerState = capture.Capture(
                    destination.ProviderName,
                    destination.StreamNamespace,
                    destination.StreamId,
                    serializedEvent,
                    requestContext,
                    out var captureFailure);
                failureReason ??= captureFailure;
            }

            destinationStates.Add(new OrleansDestinationPlanState(
                GetDestinationKey(destination),
                destination.ProviderName,
                destination.StreamNamespace,
                destination.StreamId,
                providerState,
                requestContext,
                failureReason));
        }

        var resolverServiceId = _serviceIdProvider.GetCurrentServiceId();
        if (!string.Equals(resolverServiceId, serviceId, StringComparison.Ordinal))
        {
            return new ExecutorSizeDestinationPlan(
                resolverServiceId,
                destinations.Select(GetDestinationKey).ToArray(),
                destinationStates)
            {
                PreparedEvent = serializedEvent
            };
        }

        return new ExecutorSizeDestinationPlan(
            serviceId,
            destinations.Select(GetDestinationKey).ToArray(),
            destinationStates)
        {
            PreparedEvent = serializedEvent
        };
    }

    public async Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        IReadOnlyDictionary<Guid, ExecutorSizeDestinationPlan> destinationPlans,
        CancellationToken cancellationToken = default)
    {
        foreach (var (evt, _) in events)
        {
            if (!destinationPlans.TryGetValue(evt.Id, out var plan) ||
                plan.ProviderState is not IReadOnlyList<OrleansDestinationPlanState> destinations)
            {
                throw new InvalidOperationException(
                    $"The captured destination plan for event {evt.Id} is unavailable.");
            }

            var serializableEvent = plan.PreparedEvent ?? evt.ToSerializableEvent(_domainTypes.EventTypes);
            foreach (var destination in destinations)
            {
                var requestContext = destination.RequestContext is null
                    ? null
                    : destination.RequestContext.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal);
                PlannedPublishObserver?.Invoke(
                    serializableEvent,
                    destination.ProviderName,
                    destination.StreamNamespace,
                    destination.StreamId,
                    requestContext);
                _channel.Writer.TryWrite(new PublishItem(
                    destination.ProviderName,
                    destination.StreamNamespace,
                    destination.StreamId,
                    serializableEvent,
                    0,
                    requestContext));
            }
        }

        await Task.CompletedTask;
    }

    private static string GetDestinationKey(OrleansDestination destination) =>
        $"{destination.ProviderName}|{destination.StreamNamespace}|{destination.StreamId:D}";

    private sealed record OrleansDestination(
        string ProviderName,
        string StreamNamespace,
        Guid StreamId);

    private async Task ProcessQueueAsync()
    {
        const int baseDelayMs = 100; // base backoff
        const int maxDelayMs = 5000; // cap

        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            try
            {
                var provider = _clusterClient.GetStreamProvider(item.Provider);
                var stream = provider.GetStream<SerializableEvent>(StreamId.Create(item.Namespace, item.StreamId));
                var priorContext = _deepCopier is null ? null : RequestContextExtensions.Export(_deepCopier);
                try
                {
                    if (item.RequestContext is not null)
                    {
                        RequestContextExtensions.Import(item.RequestContext);
                    }

                    await stream.OnNextAsync(item.Event);
                }
                finally
                {
                    if (priorContext is not null)
                    {
                        RequestContextExtensions.Import(priorContext);
                    }
                    else
                    {
                        RequestContext.Clear();
                    }
                }
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace("Published event {EventId} to {Provider}/{Namespace}/{StreamId}",
                        item.Event.Id, item.Provider, item.Namespace, item.StreamId);
                }
            }
            catch (Exception ex)
            {
                var nextAttempt = item.Attempt + 1;
                var delay = Math.Min(maxDelayMs, baseDelayMs * (int)Math.Pow(2, Math.Min(10, item.Attempt)));
                _logger.LogWarning(ex,
                    "Publish failed for event {EventId} (attempt {Attempt}). Retrying in {Delay} ms",
                    item.Event.Id, nextAttempt, delay);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay);
                        // re-enqueue with incremented attempt
                        _channel.Writer.TryWrite(item with { Attempt = nextAttempt });
                    }
                    catch (Exception delayEx)
                    {
                        _logger.LogError(delayEx, "Failed to re-enqueue event {EventId}", item.Event.Id);
                    }
                });
            }
        }
    }
}
