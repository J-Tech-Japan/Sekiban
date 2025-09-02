using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Linq;
using System.Text;
using System.Text.Json;
using Sekiban.Dcb.Snapshots;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General actor implementation that manages a single multi-projector instance by name.
///     Maintains both safe and unsafe states with event buffering for handling out-of-order events.
/// </summary>
public class GeneralMultiProjectionActor
{

    private readonly DcbDomainTypes _domain;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GeneralMultiProjectionActorOptions _options;
    private readonly string _projectorName;
    private readonly IMultiProjectorTypes _types;

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
        GeneralMultiProjectionActorOptions? options = null)
    {
        _types = domainTypes.MultiProjectorTypes;
        _domain = domainTypes;
        _jsonOptions = domainTypes.JsonSerializerOptions;
        _projectorName = projectorName;
        _options = options ?? new GeneralMultiProjectionActorOptions();
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

        // Use projector-defined deserializer if available
        var payloadJson = Encoding.UTF8.GetString(state.Payload);
        var projTypeRb = _types.GetProjectorType(state.ProjectorName);
        if (!projTypeRb.IsSuccess) throw projTypeRb.GetException();
        var projType = projTypeRb.GetValue();
        // Use the new Deserialize method from IMultiProjectorTypes
    var safeThreshold = GetSafeWindowThreshold();
    var deserializeResult = _types.Deserialize(state.ProjectorName, _domain, safeThreshold.Value, payloadJson);
        if (!deserializeResult.IsSuccess) throw deserializeResult.GetException();
        
        var loadedPayload = deserializeResult.GetValue();
        Console.WriteLine($"[{_projectorName}] Deserialize: via IMultiProjectorTypes");

        // Check if the payload type implements ISafeAndUnsafeStateAccessor
        var payloadType = loadedPayload.GetType();
        var accessorInterfaces = payloadType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISafeAndUnsafeStateAccessor<>))
            .ToList();

        if (accessorInterfaces.Any())
        {
            // The payload already implements ISafeAndUnsafeStateAccessor, use it directly
            _singleStateAccessor = loadedPayload;
        }
        else
        {
            // Create a wrapper and set its state
            // For now, we create a fresh wrapper with the loaded state as both safe and unsafe
            // This is acceptable because snapshots should only contain safe states
            var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payloadType);
            _singleStateAccessor = Activator.CreateInstance(
                wrapperType,
                loadedPayload,
                _projectorName,
                _types,
                _jsonOptions,
                state.Version,
                state.LastEventId,
                state.LastSortableUniqueId) as IMultiProjectionPayload;

            if (_singleStateAccessor == null)
            {
                throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
            }
        }

        // Update tracking variables
        _unsafeLastEventId = state.LastEventId;
        _unsafeLastSortableUniqueId = state.LastSortableUniqueId;
        _unsafeVersion = state.Version;

        // Restore catching up state
        _isCatchedUp = state.IsCatchedUp;

        return Task.CompletedTask;
    }

    // Compatibility restore: ignore projector version mismatch and restore snapshot payload as-is
    public Task SetCurrentStateIgnoringVersion(SerializableMultiProjectionState state)
    {
        var payloadJson = Encoding.UTF8.GetString(state.Payload);
        var projTypeRb = _types.GetProjectorType(state.ProjectorName);
        if (!projTypeRb.IsSuccess) throw projTypeRb.GetException();
        var projType = projTypeRb.GetValue();
        // Use the new Deserialize method from IMultiProjectorTypes
    var safeThreshold = GetSafeWindowThreshold();
    var deserializeResult = _types.Deserialize(state.ProjectorName, _domain, safeThreshold.Value, payloadJson);
        if (!deserializeResult.IsSuccess) throw deserializeResult.GetException();
        
        var loadedPayload = deserializeResult.GetValue();
        Console.WriteLine($"[{_projectorName}] Deserialize(ignoreVersion): via IMultiProjectorTypes");

        var payloadType = loadedPayload.GetType();
        var accessorInterfaces = payloadType
            .GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISafeAndUnsafeStateAccessor<>))
            .ToList();

        if (accessorInterfaces.Any())
        {
            _singleStateAccessor = loadedPayload;
        }
        else
        {
            var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payloadType);
            _singleStateAccessor = Activator.CreateInstance(
                wrapperType,
                loadedPayload,
                _projectorName,
                _types,
                _jsonOptions,
                state.Version,
                state.LastEventId,
                state.LastSortableUniqueId) as IMultiProjectionPayload;
            if (_singleStateAccessor == null)
            {
                throw new InvalidOperationException($"Failed to create wrapper for projector {_projectorName}");
            }
        }

        _unsafeLastEventId = state.LastEventId;
        _unsafeLastSortableUniqueId = state.LastSortableUniqueId;
        _unsafeVersion = state.Version;
        _isCatchedUp = state.IsCatchedUp;

        return Task.CompletedTask;
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
            // Use the new Serialize method from IMultiProjectorTypes
            var safeThreshold = GetSafeWindowThreshold();
            var serializeResult = _types.Serialize(_projectorName, _domain, safeThreshold.Value, payload);
            if (!serializeResult.IsSuccess)
            {
                return ResultBox.Error<SerializableMultiProjectionState>(serializeResult.GetException());
            }
            
            var json = serializeResult.GetValue();
            Console.WriteLine($"[{_projectorName}] Serialize: via IMultiProjectorTypes");
            var bytes = Encoding.UTF8.GetBytes(json);
            var payloadType = payload.GetType();
            var state = new SerializableMultiProjectionState(
                bytes,
                payloadType.FullName ?? payloadType.Name,
                _projectorName,
                projectorVersion,
                multiProjectionState.LastSortableUniqueId,
                multiProjectionState.LastEventId,
                multiProjectionState.Version,
                _isCatchedUp,
                multiProjectionState.IsSafeState);
            return ResultBox.FromValue(state);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableMultiProjectionState>(ex);
        }
    }

    /// <summary>
    ///     Returns a snapshot envelope using the actor's configured offload settings. Grain callers can persist
    ///     this envelope as-is without caring whether the payload was offloaded.
    /// </summary>
    public async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSnapshotAsync(
        bool canGetUnsafeState = true,
        CancellationToken cancellationToken = default)
    {
        // If accessor or threshold is not configured, return inline snapshot
        if (_options.SnapshotAccessor == null || _options.SnapshotOffloadThresholdBytes <= 0)
        {
            var inline = await BuildSerializableStateAsync(canGetUnsafeState);
            if (!inline.IsSuccess)
                return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(inline.GetException());
            return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(false, inline.GetValue(), null));
        }

        // Use configured offload policy
        return await GetSerializableStateWithOffloadAsync(
            _options.SnapshotAccessor,
            _options.SnapshotOffloadThresholdBytes,
            canGetUnsafeState,
            cancellationToken);
    }

    /// <summary>
    ///     Restores this actor from a snapshot envelope. If offloaded, reads payload via configured accessor.
    /// </summary>
    public async Task SetSnapshotAsync(SerializableMultiProjectionStateEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!envelope.IsOffloaded)
        {
            if (envelope.InlineState == null)
                throw new InvalidOperationException("Inline snapshot missing InlineState");
            await SetCurrentState(envelope.InlineState);
            return;
        }

        // Offloaded path
        if (_options.SnapshotAccessor == null)
            throw new InvalidOperationException("SnapshotAccessor is not configured for offloaded snapshot restore");

        if (envelope.OffloadedState == null)
            throw new InvalidOperationException("Offloaded snapshot missing OffloadedState");

        var bytes = await _options.SnapshotAccessor.ReadAsync(envelope.OffloadedState.OffloadKey, cancellationToken);

        var reconstructed = new SerializableMultiProjectionState(
            bytes,
            envelope.OffloadedState.MultiProjectionPayloadType,
            envelope.OffloadedState.ProjectorName,
            envelope.OffloadedState.ProjectorVersion,
            envelope.OffloadedState.LastSortableUniqueId,
            envelope.OffloadedState.LastEventId,
            envelope.OffloadedState.Version,
            envelope.OffloadedState.IsCatchedUp,
            envelope.OffloadedState.IsSafeState);

        await SetCurrentState(reconstructed);
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
        // Phase1 safeguard: promote buffered events before building a safe snapshot
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

    /// <summary>
    ///     Create a serializable snapshot. If the JSON size exceeds thresholdBytes, offload the payload to the provided
    ///     blob storage and return an offloaded envelope.
    /// </summary>
    private async Task<ResultBox<SerializableMultiProjectionStateEnvelope>> GetSerializableStateWithOffloadAsync(
        IBlobStorageSnapshotAccessor blobAccessor,
        int thresholdBytes,
        bool canGetUnsafeState,
        CancellationToken cancellationToken)
    {
        var stateResult = await BuildSerializableStateAsync(canGetUnsafeState);
        if (!stateResult.IsSuccess)
        {
            return ResultBox.Error<SerializableMultiProjectionStateEnvelope>(stateResult.GetException());
        }

        var inline = stateResult.GetValue();

        // Estimate full JSON size as currently stored to Orleans (baseline)
        var json = JsonSerializer.Serialize(inline, _jsonOptions);
        var jsonSize = Encoding.UTF8.GetByteCount(json);

        if (jsonSize <= thresholdBytes)
        {
            return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
                IsOffloaded: false,
                InlineState: inline,
                OffloadedState: null));
        }

        // Offload only the payload bytes to minimize storage
        var key = await blobAccessor.WriteAsync(inline.Payload, _projectorName, cancellationToken);
        var offloaded = new SerializableMultiProjectionStateOffloaded(
            key,
            blobAccessor.ProviderName,
            inline.MultiProjectionPayloadType,
            inline.ProjectorName,
            inline.ProjectorVersion,
            inline.LastSortableUniqueId,
            inline.LastEventId,
            inline.Version,
            inline.IsCatchedUp,
            inline.IsSafeState,
            inline.Payload.LongLength);

        return ResultBox.FromValue(new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: true,
            InlineState: null,
            OffloadedState: offloaded));
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
        // Cache the method reference outside the loop
        var accessorType = _singleStateAccessor!.GetType();
        var method = accessorType.GetMethod("ProcessEvent");
        if (method == null)
        {
            throw new InvalidOperationException($"ProcessEvent method not found on {accessorType.Name} for projector {_projectorName}");
        }
        
        // Process events through the single state accessor
        foreach (var ev in events)
        {
            try
            {
                var result = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold, _domain });
                    
                // ProcessEvent returns ISafeAndUnsafeStateAccessor<T> where T implements IMultiProjectionPayload
                // For DualStateProjectionWrapper, it returns 'this' which implements both interfaces
                if (result != null)
                {
                    // The result should be the same object or a new instance that implements IMultiProjectionPayload
                    // For DualStateProjectionWrapper.ProcessEvent, it returns 'this' so the object reference shouldn't change
                    // But we still need to handle the case where a new instance might be returned
                    if (result is IMultiProjectionPayload payload)
                    {
                        _singleStateAccessor = payload;
                    }
                    else
                    {
                        // This should not happen with proper implementation, but log for debugging
                        throw new InvalidOperationException($"ProcessEvent returned incompatible type for projector {_projectorName}: {result.GetType().FullName}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"ProcessEvent returned null for projector {_projectorName}");
                }

                // Update tracking - these are managed separately from the wrapper's internal state
                _unsafeLastEventId = ev.Id;
                // Keep monotonic max for sortable unique id
                if (string.IsNullOrEmpty(_unsafeLastSortableUniqueId) ||
                    string.Compare(ev.SortableUniqueIdValue, _unsafeLastSortableUniqueId, StringComparison.Ordinal) > 0)
                {
                    _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
                }
                _unsafeVersion++;
            }
            catch (Exception ex)
            {
                // Log the error with event details for debugging
                var innerEx = ex.InnerException ?? ex;
                throw new InvalidOperationException(
                    $"Failed to process event {ev.Id} ({ev.EventType}) in projector {_projectorName}: {innerEx.Message}",
                    innerEx);
            }
        }
    }

    // Phase1: forcibly process buffered events by requesting a safe projection threshold evaluation
    private void ForcePromoteBufferedEvents()
    {
        if (_singleStateAccessor == null) return;
        try
        {
            // Request safe projection which triggers buffered event promotion inside wrapper
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

    // Debug helper: force promotion with artificially large safe window (promote everything)
    private void ForcePromoteAllBufferedEvents()
    {
        if (_singleStateAccessor == null) return;
        try
        {
            var accessorType = _singleStateAccessor.GetType();
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            if (getSafeMethod != null)
            {
                // Use MaxValue threshold so全イベントが IsEarlierThanOrEqual となり昇格
                var maxThreshold = SortableUniqueId.MaxValue;
                _ = getSafeMethod.Invoke(_singleStateAccessor, new object[] { maxThreshold, _domain });
                Console.WriteLine($"[SafePromotion] projector={_projectorName} force-promote-all invoked threshold={maxThreshold.Value}");
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

        // Use reflection to get the appropriate state with explicit metadata
        var accessorType = _singleStateAccessor!.GetType();
        IMultiProjectionPayload statePayload;
        bool isSafeState;
        string lastSortableId;
        Guid lastEventId;
        int stateVersion;

        if (canGetUnsafeState)
        {
            // Auto-promotion: unsafe取得要求でも古いバッファを昇格させる (SafeProjection 呼び出しで内部 ProcessBufferedEvents 実行)
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
            // projection has properties: State, LastSortableUniqueId, LastEventId, Version
            statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
            lastSortableId = (string)(projection?.GetType().GetProperty("LastSortableUniqueId")?.GetValue(projection) ?? _unsafeLastSortableUniqueId);
            lastEventId = (Guid)(projection?.GetType().GetProperty("LastEventId")?.GetValue(projection) ?? _unsafeLastEventId);
            stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);
            
            // Check if the unsafe state is actually safe (no events within safe window)
            // This happens when all events are outside the safe window
            if (!string.IsNullOrEmpty(lastSortableId))
            {
                var lastEventTime = new SortableUniqueId(lastSortableId).GetDateTime();
                var safeThresholdTime = safeWindowThreshold.GetDateTime();
                // If the last event is before the safe threshold, it means there are no unsafe events
                isSafeState = lastEventTime <= safeThresholdTime;
            }
            else
            {
                // No events at all, so it's safe
                isSafeState = true;
            }
        }
        else
        {
            var getSafeMethod = accessorType.GetMethod("GetSafeProjection");
            var projection = getSafeMethod?.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain });
            // projection has properties: State, SafeLastSortableUniqueId, Version
            statePayload = (IMultiProjectionPayload)(projection?.GetType().GetProperty("State")?.GetValue(projection) ?? _singleStateAccessor);
            lastSortableId = (string)(projection?.GetType().GetProperty("SafeLastSortableUniqueId")?.GetValue(projection) ?? _unsafeLastSortableUniqueId);
            lastEventId = Guid.Empty;
            stateVersion = (int)(projection?.GetType().GetProperty("Version")?.GetValue(projection) ?? _unsafeVersion);
            isSafeState = true;
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

            // Check if the payload type implements ISafeAndUnsafeStateAccessor
            var payloadType = initialPayload.GetType();
            var accessorInterfaces = payloadType
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISafeAndUnsafeStateAccessor<>))
                .ToList();

            if (accessorInterfaces.Any())
            {
                // The payload already implements ISafeAndUnsafeStateAccessor, use it directly
                _singleStateAccessor = initialPayload;
            } 
            else
            {
                // Wrap traditional projections in DualStateProjectionWrapper
                // Use reflection to create the generic wrapper
                var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payloadType);
                _singleStateAccessor = Activator.CreateInstance(
                    wrapperType,
                    initialPayload,
                    _projectorName,
                    _types,
                    _jsonOptions,
                    0,  // initialVersion
                    Guid.Empty,  // initialLastEventId
                    string.Empty  // initialLastSortableUniqueId
                ) as IMultiProjectionPayload;
                
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
        try
        {
            Console.WriteLine($"[SafeWindow] projector={_projectorName} baseMs={_options.SafeWindowMs} effectiveMs={effectiveWindow} emaLagMs={_observedLagMs:F1} maxLagMs={_maxLagMs:F1} now={DateTime.UtcNow:O} threshold={threshold:O}");
        }
        catch { }
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
        // Reuse LagDecayPerSecond unless a separate option is added later
        var decay = Math.Clamp(_options.LagDecayPerSecond, 0.5, 1.0);
        var factor = Math.Pow(decay, seconds);
        return Math.Max(0, _maxLagMs * factor);
    }

}
