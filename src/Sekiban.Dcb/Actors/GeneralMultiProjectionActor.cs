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

    public async Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true)
    {
        // Initialize projectors if needed
        InitializeProjectorsIfNeeded();

        // Update catching up state
        _isCatchedUp = finishedCatchUp;

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

        var rb = _types.Deserialize(state.Payload, state.ProjectorName, _jsonOptions);
        if (!rb.IsSuccess)
        {
            throw rb.GetException();
        }

        var loadedPayload = rb.GetValue();

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
            var json = JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            var state = new SerializableMultiProjectionState(
                bytes,
                payload.GetType().FullName ?? payload.GetType().Name,
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

        // Use SAFE state progress for correctness. This avoids false positives
        // when out-of-order events have been applied only to the unsafe state.
        var stateRb = await GetStateAsync(canGetUnsafeState: false);
        if (!stateRb.IsSuccess)
        {
            return false;
        }
        var safeLast = stateRb.GetValue().LastSortableUniqueId ?? string.Empty;
        if (string.IsNullOrEmpty(safeLast)) return false;
        return string.Compare(sortableUniqueId, safeLast, StringComparison.Ordinal) <= 0;
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
        // Process events through the single state accessor
        foreach (var ev in events)
        {
            try
            {
                // Use reflection to call ProcessEvent method with domainTypes
                var accessorType = _singleStateAccessor!.GetType();
                var method = accessorType.GetMethod("ProcessEvent");
                if (method != null)
                {
                    var result = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold, _domain, TimeProvider.System });
                    
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
                }
                else
                {
                    throw new InvalidOperationException($"ProcessEvent method not found on {accessorType.Name} for projector {_projectorName}");
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

        // Use reflection to get the appropriate state
        var accessorType = _singleStateAccessor!.GetType();
        IMultiProjectionPayload statePayload;
        bool isSafeState;

        // Check if there are buffered events (for wrapped projections)
        var hasBufferedEventsMethod = accessorType.GetMethod("HasBufferedEvents");
        var hasBufferedEvents = hasBufferedEventsMethod != null && 
            (bool)(hasBufferedEventsMethod.Invoke(_singleStateAccessor, null) ?? false);

        if (canGetUnsafeState && hasBufferedEvents)
        {
            // Return unsafe state when requested and there are buffered events
            var getUnsafeMethod = accessorType.GetMethod("GetUnsafeState");
            statePayload
                = (IMultiProjectionPayload)(getUnsafeMethod?.Invoke(_singleStateAccessor, new object[] { _domain, TimeProvider.System }) ??
                    _singleStateAccessor);
            isSafeState = false;
        } else
        {
            // Return safe state when:
            // 1. canGetUnsafeState is false, OR
            // 2. there are no buffered events (even if canGetUnsafeState is true)
            var getSafeMethod = accessorType.GetMethod("GetSafeState");
            statePayload
                = (IMultiProjectionPayload)(getSafeMethod?.Invoke(_singleStateAccessor, new object[] { safeWindowThreshold, _domain, TimeProvider.System }) ?? _singleStateAccessor);
            isSafeState = true;
        }

        // Get version info
        var getVersionMethod = accessorType.GetMethod("GetVersion");
        var getLastEventIdMethod = accessorType.GetMethod("GetLastEventId");
        var getLastSortableIdMethod = accessorType.GetMethod("GetLastSortableUniqueId");

        var stateVersion = (int)(getVersionMethod?.Invoke(_singleStateAccessor, null) ?? _unsafeVersion);
        var lastEventId = (Guid)(getLastEventIdMethod?.Invoke(_singleStateAccessor, null) ?? _unsafeLastEventId);
        var lastSortableId = (string)(getLastSortableIdMethod?.Invoke(_singleStateAccessor, null) ??
            _unsafeLastSortableUniqueId);

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
        var threshold = DateTime.UtcNow.AddMilliseconds(-_options.SafeWindowMs);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }

}
