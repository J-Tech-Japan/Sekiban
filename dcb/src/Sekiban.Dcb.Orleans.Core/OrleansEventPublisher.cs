using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Tags;
using System.Threading.Channels;

namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Publishes Sekiban DCB events to Orleans streams using bounded, destination-owned retry queues.
/// </summary>
public class OrleansEventPublisher : IEventPublisher, IExecutorSizePreparedDestinationPublisher, IAsyncDisposable,
    IOrleansPublisherDiagnostics
{
    private readonly IStreamDestinationResolver _resolver;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ILogger<OrleansEventPublisher> _logger;
    private readonly IOrleansStreamSender _sender;
    private readonly OrleansEventPublisherOptions _options;
    private readonly OrleansPublisherDiagnosticsRecorder _diagnostics;
    private readonly object _queueGate = new();
    private readonly Dictionary<string, DestinationState> _destinations = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _activeWorkers = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private Task? _disposeTask;
    private int _totalLiveItems;
    private bool _disposed;
    private bool _pumpFaulted;

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

    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger)
        : this(clusterClient, resolver, domainTypes, logger, options: null, serviceIdProvider: null)
    {
    }

    /// <summary>Additive constructor retaining the resolver/service identity used by publication.</summary>
    [ActivatorUtilitiesConstructor]
    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        IServiceIdProvider? serviceIdProvider = null)
        : this(clusterClient, resolver, domainTypes, logger, options: null, serviceIdProvider)
    {
    }

    /// <summary>
    /// Additive bounded-publisher configuration. A null options value selects the validated defaults, which keeps
    /// options-free dependency-injection activation compatible with the original registration.
    /// </summary>
    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        OrleansEventPublisherOptions? options,
        IServiceIdProvider? serviceIdProvider = null)
        : this(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            options ?? new OrleansEventPublisherOptions(),
            serviceIdProvider ?? new DefaultServiceIdProvider(),
            sender: null,
            delayAsync: null,
            diagnostics: null)
    {
    }

    /// <summary>Internal deterministic seam for Orleans tests; production always uses the real stream sender.</summary>
    internal OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        OrleansEventPublisherOptions options,
        IServiceIdProvider serviceIdProvider,
        IOrleansStreamSender? sender,
        Func<TimeSpan, CancellationToken, Task>? delayAsync,
        OrleansPublisherDiagnosticsRecorder? diagnostics = null)
    {
        if (sender is null)
            ArgumentNullException.ThrowIfNull(clusterClient);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(domainTypes);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceIdProvider);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _resolver = resolver;
        _domainTypes = domainTypes;
        _logger = logger;
        _serviceIdProvider = serviceIdProvider;
        _diagnostics = diagnostics ?? new OrleansPublisherDiagnosticsRecorder(_options);
        var deepCopier = sender is null ? clusterClient!.ServiceProvider.GetService<DeepCopier>() : null;
        _sender = sender ?? new ClusterOrleansStreamSender(clusterClient!, deepCopier);
        _delayAsync = delayAsync ?? Task.Delay;
    }

    public async Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        foreach (var (evt, tags) in events)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            SerializableEvent serializableEvent;
            try
            {
                serializableEvent = evt.ToSerializableEvent(_domainTypes.EventTypes);
            }
            catch (Exception ex)
            {
                RecordAdmissionFailure(null, evt.Id, "admission-serialize-failed", ex);
                continue;
            }

            IReadOnlyList<OrleansPublishDestination> destinations;
            try
            {
                destinations = ResolveDestinations(evt, tags, _serviceIdProvider.GetCurrentServiceId());
            }
            catch (Exception ex)
            {
                RecordAdmissionFailure(null, evt.Id, "admission-resolve-failed", ex);
                continue;
            }

            var context = CaptureRequestContext();
            var payload = new SharedPublishPayload(serializableEvent, context);
            try
            {
                foreach (var destination in destinations)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    TryEnqueue(new OrleansPublishItem(destination, payload));
                }
            }
            finally
            {
                payload.Release();
            }
        }

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
        var destinations = ResolveDestinations(@event, tags, serviceId);
        if (destinations.Count == 0)
            return null;

        var captures = _sender is ClusterOrleansStreamSender clusterSender
            ? clusterSender.Captures
            : Array.Empty<IOrleansDestinationMeasurementCapture>();
        var destinationStates = new List<OrleansDestinationPlanState>(destinations.Count);
        foreach (var destination in destinations)
        {
            string? failureReason = null;
            Dictionary<string, object>? requestContext = null;
            try
            {
                requestContext = CaptureRequestContext()?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                failureReason = $"Orleans request-context capture failed with {ex.GetType().Name}";
            }

            var capture = captures.FirstOrDefault(candidate => candidate.Matches(destination.ProviderName));
            object? providerState = null;
            if (capture is null)
            {
                failureReason ??=
                    $"no destination measurement capability is registered for provider '{destination.ProviderName}'";
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
                GetLegacyDestinationKey(destination),
                destination.ProviderName,
                destination.StreamNamespace,
                destination.StreamId,
                providerState,
                requestContext,
                failureReason));
        }

        return new ExecutorSizeDestinationPlan(
            serviceId,
            destinations.Select(GetLegacyDestinationKey).ToArray(),
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
            if (cancellationToken.IsCancellationRequested)
                break;
            if (!destinationPlans.TryGetValue(evt.Id, out var plan) ||
                plan.ProviderState is not IReadOnlyList<OrleansDestinationPlanState> destinations)
            {
                throw new InvalidOperationException(
                    $"The captured destination plan for event {evt.Id} is unavailable.");
            }

            var serializableEvent = plan.PreparedEvent ?? evt.ToSerializableEvent(_domainTypes.EventTypes);
            var context = destinations.FirstOrDefault()?.RequestContext;
            var payload = new SharedPublishPayload(serializableEvent, context);
            try
            {
                foreach (var destination in destinations)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    if (destination.FailureReason is not null)
                    {
                        RecordAdmissionFailure(
                            new OrleansPublishDestination(
                                plan.ServiceId,
                                destination.ProviderName,
                                destination.StreamNamespace,
                                destination.StreamId),
                            evt.Id,
                            "admission-resolve-failed",
                            new InvalidOperationException(destination.FailureReason));
                        continue;
                    }

                    var requestContext = destination.RequestContext is null
                        ? null
                        : destination.RequestContext.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value,
                            StringComparer.Ordinal);
                    var plannedDestination = new OrleansPublishDestination(
                        plan.ServiceId,
                        destination.ProviderName,
                        destination.StreamNamespace,
                        destination.StreamId);
                    PlannedPublishObserver?.Invoke(
                        serializableEvent,
                        destination.ProviderName,
                        destination.StreamNamespace,
                        destination.StreamId,
                        requestContext);
                    var destinationPayload = ReferenceEquals(context, destination.RequestContext)
                        ? payload
                        : new SharedPublishPayload(serializableEvent, requestContext);
                    TryEnqueue(new OrleansPublishItem(plannedDestination, destinationPayload));
                    if (!ReferenceEquals(destinationPayload, payload))
                        destinationPayload.Release();
                }
            }
            finally
            {
                payload.Release();
            }
        }

        await Task.CompletedTask;
    }

    public OrleansPublisherDiagnosticsSnapshot GetSnapshot() => _diagnostics.Snapshot();

    public ValueTask DisposeAsync()
    {
        lock (_queueGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Task[] workers;
        lock (_queueGate)
        {
            _disposed = true;
            _shutdown.Cancel();
            foreach (var state in _destinations.Values)
                state.Queue.Writer.TryComplete();
            workers = _activeWorkers.ToArray();
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception ex) when (_shutdown.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Orleans publisher workers stopped during disposal.");
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private IReadOnlyList<OrleansPublishDestination> ResolveDestinations(
        Event @event,
        IReadOnlyCollection<ITag> tags,
        string serviceId)
    {
        return (_resolver.Resolve(@event, tags) ?? Enumerable.Empty<ISekibanStream>())
            .OfType<OrleansSekibanStream>()
            .Select(stream => new OrleansPublishDestination(
                serviceId,
                stream.ProviderName,
                stream.StreamNamespace,
                stream.StreamId))
            .ToArray();
    }

    private IReadOnlyDictionary<string, object>? CaptureRequestContext()
    {
        if (_sender is not ClusterOrleansStreamSender clusterSender || clusterSender.DeepCopier is null)
            return null;
        return RequestContextExtensions.Export(clusterSender.DeepCopier);
    }

    private bool TryEnqueue(OrleansPublishItem item)
    {
        lock (_queueGate)
        {
            if (_disposed || _pumpFaulted)
            {
                _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, "pump-faulted", 0);
                return false;
            }

            if (!_destinations.TryGetValue(item.Destination.DestinationKey, out var state))
            {
                state = new DestinationState(
                    Channel.CreateBounded<OrleansPublishItem>(new BoundedChannelOptions(
                        _options.MaxQueuedItemsPerDestination)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = false
                    }));
                _destinations.Add(item.Destination.DestinationKey, state);
            }

            if (state.LiveCount >= _options.MaxQueuedItemsPerDestination ||
                _totalLiveItems >= _options.MaxQueuedItemsTotal)
            {
                _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, "admission-capacity", 0);
                return false;
            }

            item.Payload.Retain();
            state.LiveCount++;
            _totalLiveItems++;
            if (!state.Queue.Writer.TryWrite(item))
            {
                state.LiveCount--;
                _totalLiveItems--;
                item.Payload.Release();
                _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, "admission-capacity", 0);
                return false;
            }

            if (state.Worker is null || state.Worker.IsCompleted)
            {
                state.Worker = Task.Run(() => ProcessDestinationAsync(item.Destination.DestinationKey, state));
                _activeWorkers.Add(state.Worker);
                _ = state.Worker.ContinueWith(
                    completed =>
                    {
                        lock (_queueGate)
                            _activeWorkers.Remove(completed);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return true;
        }
    }

    private async Task ProcessDestinationAsync(string destinationKey, DestinationState state)
    {
        try
        {
            while (await state.Queue.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                while (state.Queue.Reader.TryRead(out var item))
                {
                    try
                    {
                        await ProcessItemAsync(item).ConfigureAwait(false);
                    }
                    catch
                    {
                        ReleaseLive(destinationKey, state);
                        throw;
                    }
                    if (ReleaseLive(destinationKey, state))
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Disposal owns cancellation. The finally block releases any items that never reached the sender.
        }
        catch (Exception ex)
        {
            lock (_queueGate)
                _pumpFaulted = true;
            _logger.LogError(ex, "Orleans publisher destination pump faulted for {DestinationKey}.", destinationKey);
            DrainState(destinationKey, state, "pump-faulted");
        }
        finally
        {
            DrainState(destinationKey, state, null);
        }
    }

    private async Task ProcessItemAsync(OrleansPublishItem item)
    {
        var attempts = 0;
        try
        {
            for (var attempt = 1; attempt <= _options.MaxPublishAttempts; attempt++)
            {
                attempts = attempt;
                _diagnostics.Attempt(item.Destination);
                try
                {
                    await _sender.SendAsync(item.Destination, item.Payload, _shutdown.Token).ConfigureAwait(false);
                    _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, "writer-completed", attempts);
                    return;
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == _options.MaxPublishAttempts)
                    {
                        _logger.LogWarning(
                            ex,
                            "Orleans notification attempts exhausted for {EventId} at {DestinationKey}.",
                            item.Payload.Event.Id,
                            item.Destination.DestinationKey);
                        _diagnostics.Terminal(
                            item.Destination,
                            item.Payload.Event.Id,
                            "transport-attempts-exhausted",
                            attempts);
                        return;
                    }

                    var delay = ComputeDelay(attempt);
                    await _delayAsync(delay, _shutdown.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Disposal owns cancellation; the worker releases the payload in finally.
        }
        catch
        {
            _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, "pump-faulted", attempts);
            throw;
        }
        finally
        {
            item.Payload.Release();
        }
    }

    private bool ReleaseLive(string destinationKey, DestinationState state)
    {
        lock (_queueGate)
        {
            if (state.LiveCount > 0)
                state.LiveCount--;
            if (_totalLiveItems > 0)
                _totalLiveItems--;
            if (state.LiveCount != 0)
                return false;
            if (_destinations.TryGetValue(destinationKey, out var current) && ReferenceEquals(current, state))
                _destinations.Remove(destinationKey);
            return true;
        }
    }

    private void DrainState(string destinationKey, DestinationState state, string? reason)
    {
        while (state.Queue.Reader.TryRead(out var item))
        {
            item.Payload.Release();
            if (reason is not null)
                _diagnostics.Terminal(item.Destination, item.Payload.Event.Id, reason, 0);
            ReleaseLive(destinationKey, state);
        }
    }

    private TimeSpan ComputeDelay(int completedAttempt)
    {
        var multiplier = Math.Pow(2, Math.Min(20, completedAttempt - 1));
        var milliseconds = Math.Min(
            _options.MaxRetryDelay.TotalMilliseconds,
            _options.BaseRetryDelay.TotalMilliseconds * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private void RecordAdmissionFailure(
        OrleansPublishDestination? destination,
        Guid eventId,
        string reason,
        Exception exception)
    {
        _logger.LogWarning(exception, "Orleans notification admission failed with {ReasonCode}.", reason);
        _diagnostics.Terminal(destination, eventId, reason, 0);
    }

    private static string GetLegacyDestinationKey(OrleansPublishDestination destination) =>
        $"{destination.ProviderName}|{destination.StreamNamespace}|{destination.StreamId:D}";

    private sealed class DestinationState(Channel<OrleansPublishItem> queue)
    {
        public Channel<OrleansPublishItem> Queue { get; } = queue;
        public int LiveCount { get; set; }
        public Task? Worker { get; set; }
    }

    private sealed class ClusterOrleansStreamSender : IOrleansStreamSender
    {
        private readonly IClusterClient _clusterClient;
        public DeepCopier? DeepCopier { get; }
        public IReadOnlyList<IOrleansDestinationMeasurementCapture> Captures { get; }

        public ClusterOrleansStreamSender(IClusterClient clusterClient, DeepCopier? deepCopier)
        {
            _clusterClient = clusterClient;
            DeepCopier = deepCopier;
            Captures = clusterClient.ServiceProvider
                .GetServices<IOrleansDestinationMeasurementCapture>()
                .ToArray();
        }

        public async Task SendAsync(
            OrleansPublishDestination destination,
            SharedPublishPayload payload,
            CancellationToken cancellationToken)
        {
            var provider = _clusterClient.GetStreamProvider(destination.ProviderName);
            var stream = provider.GetStream<SerializableEvent>(
                StreamId.Create(destination.StreamNamespace, destination.StreamId));
            var priorContext = DeepCopier is null ? null : RequestContextExtensions.Export(DeepCopier);
            try
            {
                if (payload.RequestContext is not null)
                    RequestContextExtensions.Import(payload.RequestContext);
                await stream.OnNextAsync(payload.Event).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (priorContext is not null)
                    RequestContextExtensions.Import(priorContext);
                else
                    RequestContext.Clear();
            }
        }
    }
}
