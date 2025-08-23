using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using System.Text;
using System.Text.Json;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General actor implementation that manages a single multi-projector instance by name.
///     Maintains both safe and unsafe states with event buffering for handling out-of-order events.
/// </summary>
public class GeneralMultiProjectionActor : IMultiProjectionActorCommon
{

    // Keep track of all safe events for proper rebuilding
    private readonly Dictionary<Guid, Event> _allSafeEvents = new();

    // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
    private readonly Dictionary<Guid, Event> _bufferedEvents = new();
    private readonly DcbDomainTypes _domain;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GeneralMultiProjectionActorOptions _options;
    private readonly string _projectorName;
    private readonly IMultiProjectorTypes _types;

    // Catching up state
    private bool _isCatchedUp = true;
    private Guid _safeLastEventId;
    private string _safeLastSortableUniqueId = string.Empty;

    // Safe state - events older than SafeWindow
    private IMultiProjectionPayload? _safeProjector;
    private int _safeVersion;
    private Guid _unsafeLastEventId;
    private string _unsafeLastSortableUniqueId = string.Empty;

    // Unsafe state - includes all events
    private IMultiProjectionPayload? _unsafeProjector;
    private int _unsafeVersion;

    // Single state accessor when projection implements ISafeAndUnsafeStateAccessor
    private object? _singleStateAccessor;
    private bool _useSingleState;

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

        if (_useSingleState)
        {
            // Use single state accessor pattern
            await AddEventsWithSingleStateAsync(events, safeWindowThreshold);
        }
        else
        {
            // Use traditional dual state pattern
            await AddEventsWithDualStateAsync(events, safeWindowThreshold);
        }
    }

    private async Task AddEventsWithSingleStateAsync(IReadOnlyList<Event> events, SortableUniqueId safeWindowThreshold)
    {
        // Process events through the single state accessor
        foreach (var ev in events)
        {
            // Use reflection to call ProcessEvent method
            var accessorType = _singleStateAccessor!.GetType();
            var method = accessorType.GetMethod("ProcessEvent");
            if (method != null)
            {
                _singleStateAccessor = method.Invoke(_singleStateAccessor, new object[] { ev, safeWindowThreshold });
            }

            // Update tracking
            _unsafeLastEventId = ev.Id;
            _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
            _unsafeVersion++;
        }

        await Task.CompletedTask;
    }

    private async Task AddEventsWithDualStateAsync(IReadOnlyList<Event> events, SortableUniqueId safeWindowThreshold)
    {
        // Separate events into those that need buffering and those that don't
        var eventsToBuffer = new List<Event>();
        var safeEvents = new List<Event>();

        foreach (var ev in events)
        {
            var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);

            // Always update unsafe state
            var tags = ev.Tags.Select(tagString => _domain.TagTypes.GetTag(tagString)).ToList();
            var unsafeProjected = _types.Project(_projectorName, _unsafeProjector!, ev, tags);
            if (!unsafeProjected.IsSuccess)
            {
                throw unsafeProjected.GetException();
            }
            _unsafeProjector = unsafeProjected.GetValue();
            _unsafeLastEventId = ev.Id;
            _unsafeLastSortableUniqueId = ev.SortableUniqueIdValue;
            _unsafeVersion++;

            // Check if event is outside safe window
            if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
            {
                // Add to safe events list for batch processing
                safeEvents.Add(ev);
            } else
            {
                // Buffer event for later processing (overwrites if duplicate)
                _bufferedEvents[ev.Id] = ev;
            }
        }

        // Process safe events if any
        if (safeEvents.Count > 0)
        {
            await ProcessSafeEventsAsync(safeEvents);
        }

        // Process buffered events that are now outside safe window
        await ProcessBufferedEventsAsync();
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

        if (_useSingleState)
        {
            return GetStateFromSingleAccessorAsync(canGetUnsafeState);
        }
        else
        {
            return GetStateFromDualStateAsync(canGetUnsafeState);
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

        // Use reflection to get the appropriate state
        var accessorType = _singleStateAccessor!.GetType();
        IMultiProjectionPayload statePayload;
        
        if (canGetUnsafeState)
        {
            var getUnsafeMethod = accessorType.GetMethod("GetUnsafeState");
            statePayload = (IMultiProjectionPayload)(getUnsafeMethod?.Invoke(_singleStateAccessor, null) ?? _singleStateAccessor);
        }
        else
        {
            var getSafeMethod = accessorType.GetMethod("GetSafeState");
            statePayload = (IMultiProjectionPayload)(getSafeMethod?.Invoke(_singleStateAccessor, null) ?? _singleStateAccessor);
        }

        // Get version info
        var getVersionMethod = accessorType.GetMethod("GetVersion");
        var getLastEventIdMethod = accessorType.GetMethod("GetLastEventId");
        var getLastSortableIdMethod = accessorType.GetMethod("GetLastSortableUniqueId");
        
        var stateVersion = (int)(getVersionMethod?.Invoke(_singleStateAccessor, null) ?? _unsafeVersion);
        var lastEventId = (Guid)(getLastEventIdMethod?.Invoke(_singleStateAccessor, null) ?? _unsafeLastEventId);
        var lastSortableId = (string)(getLastSortableIdMethod?.Invoke(_singleStateAccessor, null) ?? _unsafeLastSortableUniqueId);

        var state = new MultiProjectionState(
            statePayload,
            _projectorName,
            version,
            lastSortableId,
            lastEventId,
            stateVersion,
            _isCatchedUp,
            !canGetUnsafeState // isSafeState
        );
        
        return Task.FromResult(ResultBox.FromValue(state));
    }

    private Task<ResultBox<MultiProjectionState>> GetStateFromDualStateAsync(bool canGetUnsafeState)
    {
        // Determine which state to return
        var useUnsafeState = canGetUnsafeState && _bufferedEvents.Count > 0;

        // Get version from the type
        var versionResult = _types.GetProjectorVersion(_projectorName);
        if (!versionResult.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<MultiProjectionState>(versionResult.GetException()));
        }
        var version = versionResult.GetValue();

        if (useUnsafeState)
        {
            // Return unsafe state
            var unsafeState = new MultiProjectionState(
                _unsafeProjector!,
                _projectorName,
                version,
                _unsafeLastSortableUniqueId,
                _unsafeLastEventId,
                _unsafeVersion,
                _isCatchedUp,
                false // This is unsafe state
            );
            return Task.FromResult(ResultBox.FromValue(unsafeState));
        }
        // Return safe state
        var safeState = new MultiProjectionState(
            _safeProjector!,
            _projectorName,
            version,
            _safeLastSortableUniqueId,
            _safeLastEventId,
            _safeVersion,
            _isCatchedUp // This is safe state
        );
        return Task.FromResult(ResultBox.FromValue(safeState));
    }

    public Task SetCurrentState(SerializableMultiProjectionState state)
    {
        var rb = _types.Deserialize(state.Payload, state.ProjectorName, _jsonOptions);
        if (!rb.IsSuccess)
        {
            throw rb.GetException();
        }

        // Set both safe and unsafe states to the loaded state initially
        _safeProjector = rb.GetValue();
        _safeLastEventId = state.LastEventId;
        _safeLastSortableUniqueId = state.LastSortableUniqueId;
        _safeVersion = state.Version;

        // Clone for unsafe state
        _unsafeProjector = CloneProjector(_safeProjector);
        _unsafeLastEventId = state.LastEventId;
        _unsafeLastSortableUniqueId = state.LastSortableUniqueId;
        _unsafeVersion = state.Version;

        // Restore catching up state
        _isCatchedUp = state.IsCatchedUp;

        // Clear buffered events and safe events when loading state
        _bufferedEvents.Clear();
        _allSafeEvents.Clear();
        // Note: We're losing the history of safe events when loading from snapshot
        // This is acceptable because snapshots should only contain safe states

        return Task.CompletedTask;
    }

    public Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync(bool canGetUnsafeState = true)
    {
        InitializeProjectorsIfNeeded();

        // Determine which state to serialize
        var useUnsafeState = canGetUnsafeState && _bufferedEvents.Count > 0;
        var projectorToSerialize = useUnsafeState ? _unsafeProjector! : _safeProjector!;
        var lastEventId = useUnsafeState ? _unsafeLastEventId : _safeLastEventId;
        var lastSortableId = useUnsafeState ? _unsafeLastSortableUniqueId : _safeLastSortableUniqueId;
        var version = useUnsafeState ? _unsafeVersion : _safeVersion;

        // If not allowing unsafe state and there are buffered events, only return safe state
        if (!canGetUnsafeState && _bufferedEvents.Count > 0)
        {
            // Return safe state only (for snapshots)
            projectorToSerialize = _safeProjector!;
            lastEventId = _safeLastEventId;
            lastSortableId = _safeLastSortableUniqueId;
            version = _safeVersion;
        }

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
            !useUnsafeState // IsSafeState is true when not using unsafe state
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
        if (_safeProjector == null && _singleStateAccessor == null)
        {
            var init = _types.GenerateInitialPayload(_projectorName);
            if (!init.IsSuccess)
            {
                throw init.GetException();
            }
            
            var initialPayload = init.GetValue();
            
            // Check if the payload type implements ISafeAndUnsafeStateAccessor
            var payloadType = initialPayload.GetType();
            var accessorInterfaces = payloadType.GetInterfaces()
                .Where(i => i.IsGenericType && 
                            i.GetGenericTypeDefinition() == typeof(ISafeAndUnsafeStateAccessor<>))
                .ToList();
            
            if (accessorInterfaces.Any())
            {
                // Use single state pattern
                _useSingleState = true;
                
                // Check if we can create a SafeUnsafeMultiProjectionState wrapper
                var projectorInterfaces = payloadType.GetInterfaces()
                    .Where(i => i.IsGenericType && 
                                i.GetGenericTypeDefinition() == typeof(IMultiProjector<>))
                    .ToList();
                
                if (projectorInterfaces.Any())
                {
                    // Create SafeUnsafeMultiProjectionState wrapper
                    var projectorInterface = projectorInterfaces.First();
                    var projectorType = projectorInterface.GetGenericArguments()[0];
                    var wrapperType = typeof(SafeUnsafeMultiProjectionState<>).MakeGenericType(projectorType);
                    _singleStateAccessor = Activator.CreateInstance(wrapperType, initialPayload);
                }
                else
                {
                    _singleStateAccessor = initialPayload;
                }
            }
            else
            {
                // Use traditional dual state pattern
                _useSingleState = false;
                _safeProjector = initialPayload;
                _unsafeProjector = CloneProjector(_safeProjector);
            }
        }
    }

    private async Task ProcessSafeEventsAsync(List<Event> newSafeEvents)
    {
        // Add new safe events to our collection
        foreach (var ev in newSafeEvents)
        {
            _allSafeEvents[ev.Id] = ev;
        }

        // Rebuild safe state from all safe events in chronological order
        await RebuildSafeStateAsync();
    }

    private async Task RebuildSafeStateAsync()
    {
        // Get all safe events and sort them by SortableUniqueId
        var allEvents = _allSafeEvents.Values.ToList();
        allEvents.Sort((a, b) => string.Compare(
            a.SortableUniqueIdValue,
            b.SortableUniqueIdValue,
            StringComparison.Ordinal));

        // Rebuild safe state from scratch
        var rebuiltProjector = _types.GenerateInitialPayload(_projectorName);
        if (!rebuiltProjector.IsSuccess)
        {
            throw rebuiltProjector.GetException();
        }

        var newSafeProjector = rebuiltProjector.GetValue();
        var newSafeVersion = 0;
        var newSafeLastEventId = Guid.Empty;
        var newSafeLastSortableId = string.Empty;

        foreach (var ev in allEvents)
        {
            var tags = ev.Tags.Select(tagString => _domain.TagTypes.GetTag(tagString)).ToList();
            var projected = _types.Project(_projectorName, newSafeProjector, ev, tags);
            if (!projected.IsSuccess)
            {
                throw projected.GetException();
            }
            newSafeProjector = projected.GetValue();
            newSafeLastEventId = ev.Id;
            newSafeLastSortableId = ev.SortableUniqueIdValue;
            newSafeVersion++;
        }

        _safeProjector = newSafeProjector;
        _safeLastEventId = newSafeLastEventId;
        _safeLastSortableUniqueId = newSafeLastSortableId;
        _safeVersion = newSafeVersion;

        await Task.CompletedTask;
    }

    private async Task ProcessBufferedEventsAsync()
    {
        var safeWindowThreshold = GetSafeWindowThreshold();
        var eventsToProcess = new List<Event>();
        var keysToRemove = new List<Guid>();

        // Find events that are now outside safe window
        foreach (var kvp in _bufferedEvents)
        {
            var ev = kvp.Value;
            var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);

            if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
            {
                eventsToProcess.Add(ev);
                keysToRemove.Add(kvp.Key);
            }
        }

        // Remove processed events from buffer
        foreach (var key in keysToRemove)
        {
            _bufferedEvents.Remove(key);
        }

        // Add newly safe events to our collection and rebuild
        if (eventsToProcess.Count > 0)
        {
            foreach (var ev in eventsToProcess)
            {
                _allSafeEvents[ev.Id] = ev;
            }

            // Rebuild safe state from all safe events
            await RebuildSafeStateAsync();
        }
    }

    private SortableUniqueId GetSafeWindowThreshold()
    {
        var threshold = DateTime.UtcNow.AddMilliseconds(-_options.SafeWindowMs);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }

    private IMultiProjectionPayload CloneProjector(IMultiProjectionPayload source)
    {
        // Serialize and deserialize to create a deep clone
        var json = JsonSerializer.Serialize(source, source.GetType(), _jsonOptions);
        var cloned = JsonSerializer.Deserialize(json, source.GetType(), _jsonOptions);
        return (IMultiProjectionPayload)cloned!;
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
}
