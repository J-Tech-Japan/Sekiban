using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
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
    private const string AdmissionResolveFailedReason = "admission-resolve-failed";
    private readonly IStreamDestinationResolver _resolver;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ILogger<OrleansEventPublisher> _logger;
    private readonly IOrleansStreamSender _sender;
    private readonly OrleansEventPublisherOptions _options;
    private readonly OrleansPublisherDiagnosticsRecorder _diagnostics;
    private readonly IReadOnlyList<IOrleansDestinationMeasurementCapture> _measurementCaptures;
    private readonly object _queueGate = new();
    private readonly Dictionary<string, DestinationState> _destinations = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _activeWorkers = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Func<Func<Task>, CancellationToken, Task> _startWorkerAsync;
    private readonly Func<Dictionary<string, object>?>? _captureRequestContext;
    private readonly Action? _payloadReleased;
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
        : this(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            options: new OrleansEventPublisherOptions(),
            serviceIdProvider: new DefaultServiceIdProvider())
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
        : this(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            options: new OrleansEventPublisherOptions(),
            serviceIdProvider: serviceIdProvider ?? new DefaultServiceIdProvider())
    {
    }

    /// <summary>
    /// Additive bounded-publisher configuration. The required options parameter is placed after the legacy
    /// service-provider parameter so the original five-argument null-literal call remains source-compatible.
    /// </summary>
    public OrleansEventPublisher(
        IClusterClient clusterClient,
        IStreamDestinationResolver resolver,
        DcbDomainTypes domainTypes,
        ILogger<OrleansEventPublisher> logger,
        IServiceIdProvider? serviceIdProvider,
        OrleansEventPublisherOptions options)
        : this(
            clusterClient,
            resolver,
            domainTypes,
            logger,
            options,
            serviceIdProvider ?? new DefaultServiceIdProvider())
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
        OrleansEventPublisherTestHooks? testHooks = null)
    {
        var sender = testHooks?.Sender;
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
        _diagnostics = testHooks?.Diagnostics ?? new OrleansPublisherDiagnosticsRecorder(_options);
        var deepCopier = sender is null ? clusterClient.ServiceProvider.GetService<DeepCopier>() : null;
        _sender = sender ?? new ClusterOrleansStreamSender(clusterClient, deepCopier);
        _measurementCaptures = testHooks?.MeasurementCaptures ??
            (_sender is ClusterOrleansStreamSender clusterSender
                ? clusterSender.Captures
                : Array.Empty<IOrleansDestinationMeasurementCapture>());
        _delayAsync = testHooks?.DelayAsync ?? Task.Delay;
        _startWorkerAsync = testHooks?.StartWorkerAsync ?? ((work, cancellationToken) => Task.Run(work, cancellationToken));
        _captureRequestContext = testHooks?.CaptureRequestContext;
        _payloadReleased = testHooks?.PayloadReleased;
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
                RecordAdmissionFailure(null, evt.Id, AdmissionResolveFailedReason, ex);
                continue;
            }

            Dictionary<string, object>? context;
            try
            {
                context = CaptureRequestContext(requireDeepCopier: false).Context;
            }
            catch (Exception ex)
            {
                RecordAdmissionFailure(null, evt.Id, AdmissionResolveFailedReason, ex);
                continue;
            }
            var payload = new SharedPublishPayload(serializableEvent, context, _payloadReleased);
            try
            {
                foreach (var destination in destinations)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    OrleansDestinationPlanState planState;
                    try
                    {
                        planState = PrepareDestinationPlanState(destination, context);
                    }
                    catch (Exception ex)
                    {
                        RecordAdmissionFailure(destination, evt.Id, AdmissionResolveFailedReason, ex);
                        continue;
                    }

                    if (planState.FailureReason is not null)
                    {
                        RecordAdmissionFailure(
                            destination,
                            evt.Id,
                            AdmissionResolveFailedReason,
                            new InvalidOperationException(planState.FailureReason));
                        continue;
                    }

                    TryEnqueue(new OrleansPublishItem(planState, payload));
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

        var (requestContext, contextFailure) = CapturePlanRequestContext();
        var destinationStates = destinations
            .Select(destination => CapturePlanDestinationState(
                destination,
                serializedEvent,
                requestContext,
                contextFailure,
                serviceId))
            .ToArray();

        return new ExecutorSizeDestinationPlan(
            serviceId,
            destinations.Select(destination => destination.DestinationKey).ToArray(),
            destinationStates)
        {
            PreparedEvent = serializedEvent
        };
    }

    private (IReadOnlyDictionary<string, object>? RequestContext, string? FailureReason) CapturePlanRequestContext()
    {
        try
        {
            var capture = CaptureRequestContext(requireDeepCopier: true);
            if (!capture.IsAvailable)
            {
                return (null, "Orleans request-context capture capability is unavailable");
            }

            return (capture.Context, null);
        }
        catch (Exception ex)
        {
            return (null, $"Orleans request-context capture failed with {ex.GetType().Name}");
        }
    }

    private OrleansDestinationPlanState CapturePlanDestinationState(
        OrleansPublishDestination destination,
        SerializableEvent serializedEvent,
        IReadOnlyDictionary<string, object>? requestContext,
        string? contextFailure,
        string serviceId)
    {
        string? failureReason = contextFailure;
        IOrleansPreparedStreamTarget? preparedTarget = null;
        if (failureReason is null)
        {
            try
            {
                preparedTarget = _sender.PrepareDestination(destination);
            }
            catch (Exception ex)
            {
                failureReason = $"Orleans destination preparation failed with {ex.GetType().Name}";
            }
        }

        object? providerState = null;
        if (failureReason is null)
        {
            var capture = _measurementCaptures.FirstOrDefault(
                candidate => candidate.Matches(destination.ProviderName));
            if (capture is null)
            {
                failureReason =
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
                failureReason = captureFailure;
            }
        }

        return new OrleansDestinationPlanState(
            destination.DestinationKey,
            destination.ProviderName,
            destination.StreamNamespace,
            destination.StreamId,
            providerState,
            requestContext,
            failureReason)
        {
            ServiceId = serviceId,
            PreparedTarget = preparedTarget
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

            if (plan.PreparedEvent is not { } serializableEvent)
            {
                throw new InvalidOperationException(
                    $"The captured destination plan for event {evt.Id} does not contain its prepared serialized event.");
            }

            var context = destinations.FirstOrDefault()?.RequestContext;
            var payload = new SharedPublishPayload(serializableEvent, context, _payloadReleased);
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
                            AdmissionResolveFailedReason,
                            new InvalidOperationException(destination.FailureReason));
                        continue;
                    }

                    if (destination.PreparedTarget is null)
                    {
                        throw new InvalidOperationException(
                            $"The captured destination plan for '{destination.DestinationKey}' does not contain its prepared target.");
                    }

                    PlannedPublishObserver?.Invoke(
                        serializableEvent,
                        destination.ProviderName,
                        destination.StreamNamespace,
                        destination.StreamId,
                        destination.RequestContext);
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

    public OrleansPublisherDiagnosticsSnapshot GetSnapshot() => _diagnostics.Snapshot();

    internal int DestinationStateCount
    {
        get
        {
            lock (_queueGate)
                return _destinations.Count;
        }
    }

    internal void CompleteDestinationWriterForTesting(string destinationKey)
    {
        lock (_queueGate)
        {
            if (!_destinations.TryGetValue(destinationKey, out var state))
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
                _destinations.Add(destinationKey, state);
            }

            state.Queue.Writer.TryComplete();
        }
    }

    internal static IOrleansPreparedStreamTarget CreatePreparedStreamTargetForTesting(
        IAsyncStream<SerializableEvent> stream,
        DeepCopier? deepCopier = null) =>
        new ClusterOrleansPreparedStreamTarget(stream, deepCopier);

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

    private RequestContextCapture CaptureRequestContext(bool requireDeepCopier)
    {
        if (_captureRequestContext is not null)
            return new RequestContextCapture(true, _captureRequestContext());
        if (_sender is not ClusterOrleansStreamSender clusterSender)
            return new RequestContextCapture(true, null);
        if (clusterSender.DeepCopier is null)
            return new RequestContextCapture(!requireDeepCopier, null);
        return new RequestContextCapture(true, RequestContextExtensions.Export(clusterSender.DeepCopier));
    }

    private OrleansDestinationPlanState PrepareDestinationPlanState(
        OrleansPublishDestination destination,
        IReadOnlyDictionary<string, object>? requestContext)
    {
        var preparedTarget = _sender.PrepareDestination(destination);
        return new OrleansDestinationPlanState(
            destination.DestinationKey,
            destination.ProviderName,
            destination.StreamNamespace,
            destination.StreamId,
            MeasurementState: null,
            requestContext,
            FailureReason: null)
        {
            ServiceId = destination.ServiceId,
            PreparedTarget = preparedTarget
        };
    }

    private void TryEnqueue(OrleansPublishItem item)
    {
        lock (_queueGate)
        {
            if (_disposed || _pumpFaulted)
            {
                _diagnostics.Terminal(ToPublishDestination(item.Destination), item.Payload.Event.Id, "pump-faulted", 0);
                return;
            }

            if (_totalLiveItems >= _options.MaxQueuedItemsTotal)
            {
                _diagnostics.Terminal(
                    ToPublishDestination(item.Destination),
                    item.Payload.Event.Id,
                    "admission-capacity",
                    0);
                return;
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

            if (state.LiveCount >= _options.MaxQueuedItemsPerDestination)
            {
                _diagnostics.Terminal(
                    ToPublishDestination(item.Destination),
                    item.Payload.Event.Id,
                    "admission-capacity",
                    0);
                return;
            }

            item.Payload.Retain();
            state.LiveCount++;
            _totalLiveItems++;
            if (!state.Queue.Writer.TryWrite(item))
            {
                state.LiveCount--;
                _totalLiveItems--;
                item.Payload.Release();
                if (state.LiveCount == 0 &&
                    _destinations.TryGetValue(item.Destination.DestinationKey, out var current) &&
                    ReferenceEquals(current, state))
                {
                    _destinations.Remove(item.Destination.DestinationKey);
                }
                _diagnostics.Terminal(
                    ToPublishDestination(item.Destination),
                    item.Payload.Event.Id,
                    "writer-completed",
                    0);
                return;
            }

            if (state.Worker is null || state.Worker.IsCompleted)
            {
                state.Worker = _startWorkerAsync(
                    () => ProcessDestinationAsync(item.Destination.DestinationKey, state),
                    CancellationToken.None);
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
                _diagnostics.Attempt(ToPublishDestination(item.Destination));
                try
                {
                    if (item.Destination.PreparedTarget is null)
                    {
                        throw new InvalidOperationException(
                            $"The destination plan for '{item.Destination.DestinationKey}' has no prepared target.");
                    }

                    await item.Destination.PreparedTarget
                        .SendAsync(item.Payload, _shutdown.Token)
                        .ConfigureAwait(false);
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
                            ToPublishDestination(item.Destination),
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
            _diagnostics.Terminal(
                ToPublishDestination(item.Destination),
                item.Payload.Event.Id,
                "pump-faulted",
                attempts);
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
                _diagnostics.Terminal(ToPublishDestination(item.Destination), item.Payload.Event.Id, reason, 0);
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

    private static OrleansPublishDestination ToPublishDestination(OrleansDestinationPlanState state) =>
        new(state.ServiceId, state.ProviderName, state.StreamNamespace, state.StreamId);

    private sealed class DestinationState(Channel<OrleansPublishItem> queue)
    {
        public Channel<OrleansPublishItem> Queue { get; } = queue;
        public int LiveCount { get; set; }
        public Task? Worker { get; set; }
    }

    private readonly record struct RequestContextCapture(
        bool IsAvailable,
        Dictionary<string, object>? Context);

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

        public IOrleansPreparedStreamTarget PrepareDestination(OrleansPublishDestination destination)
        {
            var provider = _clusterClient.GetStreamProvider(destination.ProviderName);
            var stream = provider.GetStream<SerializableEvent>(
                StreamId.Create(destination.StreamNamespace, destination.StreamId));
            return new ClusterOrleansPreparedStreamTarget(stream, DeepCopier);
        }
    }

    private sealed class ClusterOrleansPreparedStreamTarget(
        IAsyncStream<SerializableEvent> stream,
        DeepCopier? deepCopier) : IOrleansPreparedStreamTarget
    {
        public async Task SendAsync(SharedPublishPayload payload, CancellationToken cancellationToken)
        {
            var priorContext = deepCopier is null ? null : RequestContextExtensions.Export(deepCopier);
            try
            {
                if (payload.RequestContext is not null)
                    RequestContextExtensions.Import(payload.RequestContext);
                else
                    RequestContext.Clear();
                cancellationToken.ThrowIfCancellationRequested();
                var transportTask = stream.OnNextAsync(payload.Event);
                await transportTask.ConfigureAwait(false);
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

/// <summary>Internal deterministic hooks used only by Orleans publisher tests.</summary>
internal sealed class OrleansEventPublisherTestHooks
{
    public IOrleansStreamSender? Sender { get; init; }
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }
    public OrleansPublisherDiagnosticsRecorder? Diagnostics { get; init; }
    public Func<Func<Task>, CancellationToken, Task>? StartWorkerAsync { get; init; }
    public Func<Dictionary<string, object>?>? CaptureRequestContext { get; init; }
    public IReadOnlyList<IOrleansDestinationMeasurementCapture>? MeasurementCaptures { get; init; }
    public Action? PayloadReleased { get; init; }
}
