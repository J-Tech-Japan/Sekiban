using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Linq;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General actor implementation that manages a single multi-projector instance by name.
///     Maintains both safe and unsafe states with event buffering for handling out-of-order events.
/// </summary>
public class GeneralMultiProjectionActor : IMultiProjectionActorCommon
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

        // Always use single state accessor pattern (wrapped if necessary)
        await AddEventsWithSingleStateAsync(events, safeWindowThreshold);
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

    public Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync(bool canGetUnsafeState = true)
    {
        InitializeProjectorsIfNeeded();

        // Get state from the single accessor
        var stateResult = GetStateAsync(canGetUnsafeState);
        if (!stateResult.Result.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(stateResult.Result.GetException()));
        }

        var multiProjectionState = stateResult.Result.GetValue();
        var projectorToSerialize = multiProjectionState.Payload;
        var lastEventId = multiProjectionState.LastEventId;
        var lastSortableId = multiProjectionState.LastSortableUniqueId;
        var version = multiProjectionState.Version;

        // Use the projector name from constructor and serialize payload directly
        var name = _projectorName;

        // Get version from the type
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(versionResult.GetException()));
        }
        var projectorVersion = versionResult.GetValue();

        // Serialize directly using System.Text.Json
        byte[] payloadBytes;
        try
        {
            var json = JsonSerializer.Serialize(projectorToSerialize, projectorToSerialize.GetType(), _jsonOptions);
            payloadBytes = Encoding.UTF8.GetBytes(json);
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(ex));
        }

        var state = new SerializableMultiProjectionState(
            payloadBytes,
            projectorToSerialize.GetType().FullName ?? projectorToSerialize.GetType().Name,
            name,
            projectorVersion,
            lastSortableId,
            lastEventId,
            version,
            _isCatchedUp,
            multiProjectionState.IsSafeState
        );

        return Task.FromResult(ResultBox.FromValue(state));
    }

    public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        if (string.IsNullOrEmpty(sortableUniqueId))
        {
            return Task.FromResult(false);
        }

        // Check if the sortable unique ID has been processed in the unsafe state
        // The unsafe state contains all events including the most recent ones
        if (!string.IsNullOrEmpty(_unsafeLastSortableUniqueId))
        {
            // Compare sortable unique IDs - if the requested ID is less than or equal to 
            // the last processed ID, then it has been received
            var comparison = string.Compare(sortableUniqueId, _unsafeLastSortableUniqueId, StringComparison.Ordinal);
            return Task.FromResult(comparison <= 0);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    ///     Gets the last safe (persisted) sortable unique id. Used by hosts to persist SafeLastPosition.
    /// </summary>
    public string GetSafeLastSortableUniqueId()
    {
        InitializeProjectorsIfNeeded();
        
        // Get safe state to retrieve its last sortable unique ID
        var stateResult = GetStateAsync(canGetUnsafeState: false);
        if (stateResult.Result.IsSuccess)
        {
            return stateResult.Result.GetValue().LastSortableUniqueId;
        }
        
        return string.Empty;
    }

    private async Task AddEventsWithSingleStateAsync(IReadOnlyList<Event> events, SortableUniqueId safeWindowThreshold)
    {
        // Process events through the single state accessor
        foreach (var ev in events)
        {
            // Use reflection to call ProcessEvent method with domainTypes
            var accessorType = _singleStateAccessor!.GetType();
            var method = accessorType.GetMethod("ProcessEvent");
            if (method != null)
            {
                var result = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold, _domain, TimeProvider.System });
                
                // ProcessEvent returns ISafeAndUnsafeStateAccessor<T> where T implements IMultiProjectionPayload
                // The actual object is still the same type that implements both interfaces
                if (result != null)
                {
                    // Try to cast directly to IMultiProjectionPayload
                    _singleStateAccessor = result as IMultiProjectionPayload;
                    
                    // If direct cast fails, the result might be wrapped in the interface
                    // In that case, the actual object should still implement IMultiProjectionPayload
                    if (_singleStateAccessor == null)
                    {
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

            // Update tracking
            _unsafeLastEventId = ev.Id;
            _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
            _unsafeVersion++;
        }

        await Task.CompletedTask;
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
