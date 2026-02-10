using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General actor implementation that manages a single multi-projector instance by name.
///     Maintains both safe and unsafe states with event buffering for handling out-of-order events.
///     Uses IDualStateAccessor for type-safe access without reflection.
/// </summary>
public class GeneralMultiProjectionActor
{

    private readonly DcbDomainTypes _domain;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GeneralMultiProjectionActorOptions _options;
    private readonly string _projectorName;
    private readonly ICoreMultiProjectorTypes _types;
    private readonly ILogger _logger;

    // Catching up state
    private bool _isCatchedUp = true;

    // Single state accessor for all projections (wrapped if necessary)
    private IMultiProjectionPayload? _singleStateAccessor;
    private Guid _unsafeLastEventId;
    private string _unsafeLastSortableUniqueId = string.Empty;
    private int _unsafeVersion;

    // Dynamic SafeWindow tracking (observed stream lag)
    private double _observedLagMs; // EMA of observed lag in ms
    private DateTime _lastLagUpdateUtc = DateTime.MinValue;
    private double _maxLagMs; // Decayed running maximum of observed lag
    private DateTime _lastMaxUpdateUtc = DateTime.MinValue;

    public GeneralMultiProjectionActor(
        DcbDomainTypes domainTypes,
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        _types = domainTypes.MultiProjectorTypes;
        _domain = domainTypes;
        _jsonOptions = domainTypes.JsonSerializerOptions;
        _projectorName = projectorName;
        _options = options ?? new GeneralMultiProjectionActorOptions();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true, EventSource source = EventSource.Unknown)
    {
        // Initialize projectors if needed
        InitializeProjectorsIfNeeded();

        // Update catching up state
        _isCatchedUp = finishedCatchUp;

        // Update dynamic lag from stream if enabled
        if (_options.EnableDynamicSafeWindow && source == EventSource.Stream && events.Count > 0)
        {
            UpdateObservedLag(events);
        }

        var safeWindowThreshold = GetSafeWindowThreshold();

        // Sort incoming events by SortableUniqueId to ensure deterministic processing order
        // and avoid order-dependent anomalies under concurrent delivery
        var orderedDistinct = events
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        // Always use single state accessor pattern (wrapped if necessary)
        AddEventsWithSingleState(orderedDistinct, safeWindowThreshold);
    }

    public async Task AddSerializableEventsAsync(IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
    {
        var list = new List<Event>(events.Count);
        foreach (var se in events)
        {
            var payload
                = _domain.EventTypes.DeserializeEventPayload(
                    se.EventPayloadName,
                    Encoding.UTF8.GetString(se.Payload)) ??
                throw new InvalidOperationException($"Unknown event type: {se.EventPayloadName}");
            var ev = new Event(
                payload,
                se.SortableUniqueIdValue,
                se.EventPayloadName,
                se.Id,
                se.EventMetadata,
                se.Tags);
            list.Add(ev);
        }
        await AddEventsAsync(list, finishedCatchUp);
    }

    public Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true)
    {
        InitializeProjectorsIfNeeded();

        return GetStateFromSingleAccessorAsync(canGetUnsafeState);
    }

    public Task SetCurrentState(SerializableMultiProjectionState state)
    {
        // Validate projector version before restoring state
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            throw versionResult.GetException();
        }

        var currentVersion = versionResult.GetValue();
        if (!string.Equals(currentVersion, state.ProjectorVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Snapshot projector version mismatch. Current='{currentVersion}', Snapshot='{state.ProjectorVersion}' for projector '{_projectorName}'.");
        }

        RestorePayload(state);
        return Task.CompletedTask;
    }

    // Compatibility restore: ignore projector version mismatch and restore snapshot payload as-is
    public Task SetCurrentStateIgnoringVersion(SerializableMultiProjectionState state)
    {
        RestorePayload(state);
        return Task.CompletedTask;
    }

    private void RestorePayload(SerializableMultiProjectionState state)
    {
        var payloadBytes = state.GetPayloadBytes();
        var projTypeRb = _types.GetProjectorType(state.ProjectorName);
        if (!projTypeRb.IsSuccess) throw projTypeRb.GetException();

        var safeThreshold = GetSafeWindowThreshold();
        var deserializeResult = _types.Deserialize(state.ProjectorName, _domain, safeThreshold.Value, payloadBytes);
        if (!deserializeResult.IsSuccess) throw deserializeResult.GetException();

        var loadedPayload = deserializeResult.GetValue();
        _logger.LogDebug("[{ProjectorName}] Deserialize: via ICoreMultiProjectorTypes", _projectorName);

        if (loadedPayload is IDualStateAccessor)
        {
            _singleStateAccessor = loadedPayload;
        }
        else
        {
            _singleStateAccessor = DualStateProjectionWrapperFactory.Create(
                loadedPayload,
                _projectorName,
                _types,
                _jsonOptions,
                isRestoredFromSnapshot: true,
                initialVersion: state.Version,
                initialLastEventId: state.LastEventId,
                initialLastSortableUniqueId: state.LastSortableUniqueId);

            if (_singleStateAccessor == null)
            {
                throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
            }
        }

        // Update tracking variables
        _unsafeLastEventId = state.LastEventId;
        _unsafeLastSortableUniqueId = state.LastSortableUniqueId;
        _unsafeVersion = state.Version;
        _isCatchedUp = state.IsCatchedUp;
    }

    private async Task<ResultBox<SerializableMultiProjectionState>> BuildSerializableStateAsync(bool canGetUnsafeState)
    {
        InitializeProjectorsIfNeeded();
        var stateResult = await GetStateAsync(canGetUnsafeState);
        if (!stateResult.IsSuccess) return ResultBox.Error<SerializableMultiProjectionState>(stateResult.GetException());

        var multiProjectionState = stateResult.GetValue();
        var payload = multiProjectionState.Payload;
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess) return ResultBox.Error<SerializableMultiProjectionState>(versionResult.GetException());
        var projectorVersion = versionResult.GetValue();

        try
        {
            var safeThreshold = GetSafeWindowThreshold();
            var serializeResult = _types.Serialize(_projectorName, _domain, safeThreshold.Value, payload);
            if (!serializeResult.IsSuccess)
            {
                return ResultBox.Error<SerializableMultiProjectionState>(serializeResult.GetException());
            }

            var result = serializeResult.GetValue();
            _logger.LogDebug(
                "[{ProjectorName}] Serialize(binary): via ICoreMultiProjectorTypes len={CompressedBytes} (original={OriginalBytes}, ratio={CompressionRatio:P1})",
                _projectorName,
                result.CompressedSizeBytes,
                result.OriginalSizeBytes,
                result.CompressionRatio);
            var payloadType = payload.GetType();
            var state = SerializableMultiProjectionState.FromBytes(
                result.Data,
                payloadType.FullName ?? payloadType.Name,
                _projectorName,
                projectorVersion,
                multiProjectionState.LastSortableUniqueId,
                multiProjectionState.LastEventId,
                multiProjectionState.Version,
                _isCatchedUp,
                multiProjectionState.IsSafeState,
                result.OriginalSizeBytes,
                result.CompressedSizeBytes);
            return ResultBox.FromValue(state);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableMultiProjectionState>(ex);
        }
    }

    /// <summary>
    ///     Returns a snapshot envelope containing an inline payload.
    /// </summary>
    public async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSnapshotAsync(
        bool canGetUnsafeState = true,
        CancellationToken cancellationToken = default)
    {
        var inline = await BuildSerializableStateAsync(canGetUnsafeState);
        if (!inline.IsSuccess)
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(inline.GetException());
        return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(false, inline.GetValue(), null));
    }

    /// <summary>
    ///     Restores this actor from a snapshot envelope containing an inline payload.
    /// </summary>
    public async Task SetSnapshotAsync(SerializableMultiProjectionStateEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope.IsOffloaded)
        {
            throw new InvalidOperationException(
                "Offloaded snapshots are not supported by the actor. Restore from the state store before applying.");
        }

        if (envelope.InlineState == null)
            throw new InvalidOperationException("Inline snapshot missing InlineState");
        await SetCurrentState(envelope.InlineState);
    }

    /// <summary>
    ///     Builds a snapshot envelope (with offload if configured), serializes it to JSON, evaluates size limit,
    ///     and returns the data needed for persistence (JSON and safe position).
    ///     Size limit is controlled by GeneralMultiProjectionActorOptions.MaxSnapshotSerializedSizeBytes (<=0 to disable).
    /// </summary>
    public async Task<ResultBox<SnapshotPersistenceData>> BuildSnapshotForPersistenceAsync(
        bool canGetUnsafeState = false,
        CancellationToken cancellationToken = default)
    {
        // Promote buffered events before building a safe snapshot
        try
        {
            ForcePromoteBufferedEvents();
        }
        catch
        {
            // Ignore promotion errors to avoid blocking persistence
        }
        var envelopeRb = await GetSnapshotAsync(canGetUnsafeState, cancellationToken);
        if (!envelopeRb.IsSuccess)
        {
            return ResultBox.Error<SnapshotPersistenceData>(envelopeRb.GetException());
        }

        var envelope = envelopeRb.GetValue();
        var json = JsonSerializer.Serialize(envelope, _jsonOptions);
        var size = Encoding.UTF8.GetByteCount(json);

        if (_options.MaxSnapshotSerializedSizeBytes > 0 && size > _options.MaxSnapshotSerializedSizeBytes)
        {
            return ResultBox.Error<SnapshotPersistenceData>(
                new InvalidOperationException(
                    $"Snapshot size {size} exceeds limit {_options.MaxSnapshotSerializedSizeBytes}"));
        }

        // Safe position for hosts to persist
        var safePosition = await GetSafeLastSortableUniqueIdAsync();
        return ResultBox.FromValue(new SnapshotPersistenceData(json, size, safePosition));
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        if (string.IsNullOrEmpty(sortableUniqueId))
        {
            return false;
        }

        // Use UNSAFE progress for responsiveness: we only need to know whether
        // the actor has ingested the event, not whether it is outside the safe window.
        // This keeps waitForSortableUniqueId from blocking ~SafeWindowMs unnecessarily.
        var stateRb = await GetStateAsync(canGetUnsafeState: true);
        if (!stateRb.IsSuccess)
        {
            return false;
        }
        var last = stateRb.GetValue().LastSortableUniqueId ?? string.Empty;
        if (string.IsNullOrEmpty(last)) return false;
        return string.Compare(sortableUniqueId, last, StringComparison.Ordinal) <= 0;
    }

    /// <summary>
    ///     Gets the last safe (persisted) sortable unique id. Used by hosts to persist SafeLastPosition.
    /// </summary>
    public async Task<string> GetSafeLastSortableUniqueIdAsync()
    {
        InitializeProjectorsIfNeeded();

        // Get safe state to retrieve its last sortable unique ID
        var stateResult = await GetStateAsync(canGetUnsafeState: false);
        if (stateResult.IsSuccess)
        {
            return stateResult.GetValue().LastSortableUniqueId;
        }

        return string.Empty;
    }

    private void AddEventsWithSingleState(IReadOnlyList<Event> events, SortableUniqueId safeWindowThreshold)
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            // Use IDualStateAccessor for type-safe event processing without reflection
            foreach (var ev in events)
            {
                try
                {
                    dualAccessor = dualAccessor.ProcessEventAs(ev, safeWindowThreshold, _domain);

                    // Update tracking
                    _unsafeLastEventId = ev.Id;
                    if (string.IsNullOrEmpty(_unsafeLastSortableUniqueId) ||
                        string.Compare(ev.SortableUniqueIdValue, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0)
                    {
                        _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
                    }
                    _unsafeVersion++;
                }
                catch (Exception ex)
                {
                    var innerEx = ex.InnerException ?? ex;
                    throw new InvalidOperationException(
                        $"Failed to process event {ev.Id} ({ev.EventType}) in projector {_projectorName}: {innerEx.Message}",
                        innerEx);
                }
            }

            // The accessor may return 'this' (same instance) or a new instance
            if (dualAccessor is IMultiProjectionPayload updatedPayload)
            {
                _singleStateAccessor = updatedPayload;
            }
        }
        else
        {
            // Fallback for projectors that implement ISafeAndUnsafeStateAccessor<T> directly
            // (not wrapped in DualStateProjectionWrapper)
            var accessorType = _singleStateAccessor!.GetType();
            var method = accessorType.GetMethod("ProcessEvent");
            if (method == null)
            {
                throw new InvalidOperationException($"ProcessEvent method not found on {accessorType.Name} for projector {_projectorName}");
            }

            foreach (var ev in events)
            {
                try
                {
                    var result = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold, _domain });

                    if (result is IMultiProjectionPayload payload)
                    {
                        _singleStateAccessor = payload;
                    }
                    else if (result == null)
                    {
                        throw new InvalidOperationException($"ProcessEvent returned null for projector {_projectorName}");
                    }
                    else
                    {
                        throw new InvalidOperationException($"ProcessEvent returned incompatible type for projector {_projectorName}: {result.GetType().FullName}");
                    }

                    _unsafeLastEventId = ev.Id;
                    if (string.IsNullOrEmpty(_unsafeLastSortableUniqueId) ||
                        string.Compare(ev.SortableUniqueIdValue, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0)
                    {
                        _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
                    }
                    _unsafeVersion++;
                }
                catch (Exception ex)
                {
                    var innerEx = ex.InnerException ?? ex;
                    throw new InvalidOperationException(
                        $"Failed to process event {ev.Id} ({ev.EventType}) in projector {_projectorName}: {innerEx.Message}",
                        innerEx);
                }
            }
        }
    }

    public void ForcePromoteBufferedEvents()
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            var threshold = GetSafeWindowThreshold();
            dualAccessor.PromoteBufferedEvents(threshold, _domain);
            return;
        }

        // Fallback for non-IDualStateAccessor implementations
        if (_singleStateAccessor == null) return;
        try
        {
            var accessorType = _singleStateAccessor.GetType();
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            if (getSafeMethod != null)
            {
                var threshold = GetSafeWindowThreshold();
                _ = getSafeMethod.Invoke(_singleStateAccessor, new object[] { threshold, _domain });
            }
        }
        catch
        {
            // Swallow - best effort only
        }
    }

    public void ForcePromoteAllBufferedEvents()
    {
        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            var maxThreshold = SortableUniqueId.MaxValue;
            dualAccessor.PromoteBufferedEvents(maxThreshold, _domain);
            _logger.LogDebug(
                "[SafePromotion] projector={ProjectorName} force-promote-all invoked threshold={Threshold}",
                _projectorName,
                maxThreshold.Value);
            return;
        }

        // Fallback for non-IDualStateAccessor implementations
        if (_singleStateAccessor == null) return;
        try
        {
            var accessorType = _singleStateAccessor.GetType();
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            if (getSafeMethod != null)
            {
                var maxThreshold = SortableUniqueId.MaxValue;
                _ = getSafeMethod.Invoke(_singleStateAccessor, new object[] { maxThreshold, _domain });
                _logger.LogDebug(
                    "[SafePromotion] projector={ProjectorName} force-promote-all invoked threshold={Threshold}",
                    _projectorName,
                    maxThreshold.Value);
            }
        }
        catch { }
    }


    private Task<ResultBox<MultiProjectionState>> GetStateFromSingleAccessorAsync(bool canGetUnsafeState)
    {
        // Get version from the type
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<MultiProjectionState>(versionResult.GetException()));
        }
        var version = versionResult.GetValue();

        var safeWindowThreshold = GetSafeWindowThreshold();

        IMultiProjectionPayload statePayload;
        bool isSafeState;
        string lastSortableId;
        Guid lastEventId;
        int stateVersion;

        if (_singleStateAccessor is IDualStateAccessor dualAccessor)
        {
            // Use IDualStateAccessor for type-safe access without reflection
            if (canGetUnsafeState)
            {
                // Promote buffered events before reading unsafe state
                try { dualAccessor.PromoteBufferedEvents(safeWindowThreshold, _domain); } catch { }

                statePayload = (IMultiProjectionPayload)dualAccessor.GetUnsafeProjectorPayload();
                lastSortableId = dualAccessor.UnsafeLastSortableUniqueId;
                lastEventId = dualAccessor.UnsafeLastEventId;
                stateVersion = dualAccessor.UnsafeVersion;

                if (!string.IsNullOrEmpty(lastSortableId))
                {
                    var lastEventTime = new SortableUniqueId(lastSortableId).GetDateTime();
                    var safeThresholdTime = safeWindowThreshold.GetDateTime();
                    isSafeState = lastEventTime <= safeThresholdTime;
                }
                else
                {
                    isSafeState = true;
                }
            }
            else
            {
                // Promote buffered events and get safe state
                dualAccessor.PromoteBufferedEvents(safeWindowThreshold, _domain);

                statePayload = (IMultiProjectionPayload)dualAccessor.GetSafeProjectorPayload();
                var safeSortableId = dualAccessor.SafeLastSortableUniqueId;
                lastSortableId = string.IsNullOrEmpty(safeSortableId) ? _unsafeLastSortableUniqueId : safeSortableId;
                lastEventId = Guid.Empty;
                stateVersion = dualAccessor.SafeVersion;
                isSafeState = true;
            }
        }
        else
        {
            // Fallback: use reflection for projectors that implement ISafeAndUnsafeStateAccessor<T> directly
            var accessorType = _singleStateAccessor!.GetType();

            if (canGetUnsafeState)
            {
                try
                {
                    var autoSafeMethod = accessorType.GetMethod("GetSafeProjection");
                    if (autoSafeMethod != null)
                    {
                        _ = autoSafeMethod.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain });
                    }
                }
                catch { }
                var getUnsafeMethod = accessorType.GetMethod("GetUnsafeProjection");
                var projection = getUnsafeMethod?.Invoke(_singleStateAccessor, new object[] { _domain });
                statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
                lastSortableId = (string)(projection?.GetType().GetProperty("LastSortableUniqueId")?.GetValue(projection) ?? _unsafeLastSortableUniqueId);
                lastEventId = (Guid)(projection?.GetType().GetProperty("LastEventId")?.GetValue(projection) ?? _unsafeLastEventId);
                stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);

                if (!string.IsNullOrEmpty(lastSortableId))
                {
                    var lastEventTime = new SortableUniqueId(lastSortableId).GetDateTime();
                    var safeThresholdTime = safeWindowThreshold.GetDateTime();
                    isSafeState = lastEventTime <= safeThresholdTime;
                }
                else
                {
                    isSafeState = true;
                }
            }
            else
            {
                var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
                var projection = getSafeMethod?.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain });
                statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
                var safeSortableId = projection?.GetType().GetProperty("SafeLastSortableUniqueId")?.GetValue(projection) as string;
                lastSortableId = string.IsNullOrEmpty(safeSortableId) ? _unsafeLastSortableUniqueId : safeSortableId;
                lastEventId = Guid.Empty;
                stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);
                isSafeState = true;
            }
        }

        var state = new MultiProjectionState(
            statePayload,
            _projectorName,
            version,
            lastSortableId,
            lastEventId,
            stateVersion,
            _isCatchedUp,
            isSafeState
        );

        return Task.FromResult(ResultBox.FromValue(state));
    }


    /// <summary>
    ///     Get the unsafe state which includes all events including those within SafeWindow
    /// </summary>
    public Task<ResultBox<MultiProjectionState>> GetUnsafeStateAsync()
    {
        InitializeProjectorsIfNeeded();

        // Always get unsafe state
        return GetStateAsync(canGetUnsafeState: true);
    }

    private void InitializeProjectorsIfNeeded()
    {
        if (_singleStateAccessor == null)
        {
            var init = _types.GenerateInitialPayload(_projectorName);
            if (!init.IsSuccess)
            {
                throw init.GetException();
            }

            var initialPayload = init.GetValue();

            if (initialPayload is IDualStateAccessor)
            {
                // The payload already implements IDualStateAccessor, use it directly
                _singleStateAccessor = initialPayload;
            }
            else
            {
                // Wrap traditional projections in DualStateProjectionWrapper via factory
                _singleStateAccessor = DualStateProjectionWrapperFactory.Create(
                    initialPayload,
                    _projectorName,
                    _types,
                    _jsonOptions);

                if (_singleStateAccessor == null)
                {
                    throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
                }
            }
        }
    }




    private SortableUniqueId GetSafeWindowThreshold()
    {
        var effectiveWindow = _options.SafeWindowMs;
        if (_options.EnableDynamicSafeWindow)
        {
            var extraEma = GetDecayedObservedLagMs();
            var extraMax = GetDecayedMaxLagMs();
            var extra = Math.Min(Math.Max(extraEma, extraMax), _options.MaxExtraSafeWindowMs);
            effectiveWindow = (int)Math.Max(0, Math.Min(int.MaxValue, (long)_options.SafeWindowMs + (long)extra));
        }
        var threshold = DateTime.UtcNow.AddMilliseconds(-effectiveWindow);
        _logger.LogDebug(
            "[SafeWindow] projector={ProjectorName} baseMs={BaseMs} effectiveMs={EffectiveMs} emaLagMs={EmaLagMs:F1} maxLagMs={MaxLagMs:F1} now={Now:O} threshold={Threshold:O}",
            _projectorName,
            _options.SafeWindowMs,
            effectiveWindow,
            _observedLagMs,
            _maxLagMs,
            DateTime.UtcNow,
            threshold);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }

    /// <summary>
    ///     Public helper to obtain current safe window threshold without triggering state mutation.
    ///     (Primarily used to supply query context metadata.)
    /// </summary>
    public SortableUniqueId PeekCurrentSafeWindowThreshold() => GetSafeWindowThreshold();

    private void UpdateObservedLag(IReadOnlyList<Event> events)
    {
        var now = DateTime.UtcNow;
        // Representative lag: capped max of the batch
        double batchMax = 0;
        foreach (var ev in events)
        {
            var ts = new SortableUniqueId(ev.SortableUniqueIdValue).GetDateTime();
            var lagMs = (now - ts).TotalMilliseconds;
            if (lagMs > batchMax) batchMax = lagMs;
        }
        // Clamp to [0, MaxExtra]
        batchMax = Math.Max(0, Math.Min(batchMax, _options.MaxExtraSafeWindowMs));

        // Apply decay and update EMA
        var decayedEma = GetDecayedObservedLagMs();
        var alpha = Math.Clamp(_options.LagEmaAlpha, 0.01, 1.0);
        _observedLagMs = alpha * batchMax + (1 - alpha) * decayedEma;
        _lastLagUpdateUtc = now;

        // Update decayed running max: max(current decayed max, batchMax)
        var decayedMax = GetDecayedMaxLagMs();
        _maxLagMs = Math.Max(decayedMax, batchMax);
        _lastMaxUpdateUtc = now;
    }

    private double GetDecayedObservedLagMs()
    {
        if (_lastLagUpdateUtc == DateTime.MinValue || _observedLagMs <= 0)
        {
            return Math.Max(0, _observedLagMs);
        }
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0, (now - _lastLagUpdateUtc).TotalSeconds);
        var decay = Math.Clamp(_options.LagDecayPerSecond, 0.5, 1.0);
        var factor = Math.Pow(decay, seconds);
        return Math.Max(0, _observedLagMs * factor);
    }

    private double GetDecayedMaxLagMs()
    {
        if (_lastMaxUpdateUtc == DateTime.MinValue || _maxLagMs <= 0)
        {
            return Math.Max(0, _maxLagMs);
        }
        var now = DateTime.UtcNow;
        var seconds = Math.Max(0, (now - _lastMaxUpdateUtc).TotalSeconds);
        var decay = Math.Clamp(_options.LagDecayPerSecond, 0.5, 1.0);
        var factor = Math.Pow(decay, seconds);
        return Math.Max(0, _maxLagMs * factor);
    }

}
