using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Actors
{
    /// <summary>
    /// General actor implementation that manages a single multi-projector instance by name.
    /// Maintains both safe and unsafe states with event buffering for handling out-of-order events.
    /// </summary>
    public class GeneralMultiProjectionActor : IMultiProjectionActorCommon
    {
        private readonly IMultiProjectorTypes _types;
        private readonly DcbDomainTypes _domain;
        private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
        private readonly string _projectorName;
        private readonly GeneralMultiProjectionActorOptions _options;
        
        // Safe state - events older than SafeWindow
        private IMultiProjectorCommon? _safeProjector;
        private Guid _safeLastEventId;
        private string _safeLastSortableUniqueId = string.Empty;
        private int _safeVersion;
        
        // Unsafe state - includes all events
        private IMultiProjectorCommon? _unsafeProjector;
        private Guid _unsafeLastEventId;
        private string _unsafeLastSortableUniqueId = string.Empty;
        private int _unsafeVersion;
        
        // Buffer for events within SafeWindow (using Dictionary to handle duplicates)
        private readonly Dictionary<Guid, Event> _bufferedEvents = new();
        
        // Keep track of all safe events for proper rebuilding
        private readonly Dictionary<Guid, Event> _allSafeEvents = new();
        
        // Catching up state
        private bool _isCatchedUp = true;

        public GeneralMultiProjectionActor(DcbDomainTypes domainTypes, string projectorName, GeneralMultiProjectionActorOptions? options = null)
        {
            _types = domainTypes.MultiProjectorTypes;
            _domain = domainTypes;
            _jsonOptions = domainTypes.JsonSerializerOptions;
            _projectorName = projectorName;
            _options = options ?? new GeneralMultiProjectionActorOptions();
        }

        public async Task AddEventsAsync(System.Collections.Generic.IReadOnlyList<Event> events, bool finishedCatchUp = true)
        {
            // Initialize projectors if needed
            InitializeProjectorsIfNeeded();

            // Update catching up state
            _isCatchedUp = finishedCatchUp;

            var safeWindowThreshold = GetSafeWindowThreshold();
            
            // Separate events into those that need buffering and those that don't
            var eventsToBuffer = new List<Event>();
            var safeEvents = new List<Event>();
            
            foreach (var ev in events)
            {
                var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
                
                // Always update unsafe state
                var unsafeProjected = _types.Project(_unsafeProjector!, ev);
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
                }
                else
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

        public async Task AddSerializableEventsAsync(System.Collections.Generic.IReadOnlyList<SerializableEvent> events, bool finishedCatchUp = true)
        {
            var list = new System.Collections.Generic.List<Event>(events.Count);
            foreach (var se in events)
            {
                var payload = _domain.EventTypes.DeserializeEventPayload(se.EventPayloadName, System.Text.Encoding.UTF8.GetString(se.Payload))
                    ?? throw new InvalidOperationException($"Unknown event type: {se.EventPayloadName}");
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

            // Determine which state to return
            bool useUnsafeState = canGetUnsafeState && _bufferedEvents.Count > 0;
            
            if (useUnsafeState)
            {
                // Return unsafe state
                var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_unsafeProjector!);
                if (!projectorNameRb.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<MultiProjectionState>(projectorNameRb.GetException()));
                }

                var unsafeState = new MultiProjectionState(
                    (IMultiProjectionPayload)_unsafeProjector!,
                    projectorNameRb.GetValue(),
                    _unsafeProjector!.GetVersion(),
                    _unsafeLastSortableUniqueId,
                    _unsafeLastEventId,
                    _unsafeVersion,
                    _isCatchedUp,
                    false // This is unsafe state
                );
                return Task.FromResult(ResultBox.FromValue(unsafeState));
            }
            else
            {
                // Return safe state
                var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_safeProjector!);
                if (!projectorNameRb.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<MultiProjectionState>(projectorNameRb.GetException()));
                }

                var safeState = new MultiProjectionState(
                    (IMultiProjectionPayload)_safeProjector!,
                    projectorNameRb.GetValue(),
                    _safeProjector!.GetVersion(),
                    _safeLastSortableUniqueId,
                    _safeLastEventId,
                    _safeVersion,
                    _isCatchedUp,
                    true // This is safe state
                );
                return Task.FromResult(ResultBox.FromValue(safeState));
            }
        }

        public Task SetCurrentState(SerializableMultiProjectionState state)
        {
            var rb = _types.Deserialize(state.Payload, state.MultiProjectionPayloadType, _jsonOptions);
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
            bool useUnsafeState = canGetUnsafeState && _bufferedEvents.Count > 0;
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

            // Resolve projector name and serialize payload
            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(projectorToSerialize);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(projectorNameRb.GetException()));
            }
            var name = projectorNameRb.GetValue();

            var payloadBytesRb = _types.Serialize(projectorToSerialize, _jsonOptions);
            if (!payloadBytesRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(payloadBytesRb.GetException()));
            }

            var state = new SerializableMultiProjectionState(
                payloadBytesRb.GetValue(),
                projectorToSerialize.GetType().FullName ?? projectorToSerialize.GetType().Name,
                name,
                projectorToSerialize.GetVersion(),
                lastSortableId,
                lastEventId,
                version,
                _isCatchedUp,
                !useUnsafeState // IsSafeState is true when not using unsafe state
            );

            return Task.FromResult(ResultBox.FromValue(state));
        }
        
        /// <summary>
        /// Get the unsafe state which includes all events including those within SafeWindow
        /// </summary>
        public Task<ResultBox<MultiProjectionState>> GetUnsafeStateAsync()
        {
            InitializeProjectorsIfNeeded();

            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_unsafeProjector!);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<MultiProjectionState>(projectorNameRb.GetException()));
            }

            var state = new MultiProjectionState(
                (IMultiProjectionPayload)_unsafeProjector!,
                projectorNameRb.GetValue(),
                _unsafeProjector!.GetVersion(),
                _unsafeLastSortableUniqueId,
                _unsafeLastEventId,
                _unsafeVersion,
                _isCatchedUp,
                false // This is unsafe state
            );
            return Task.FromResult(ResultBox.FromValue(state));
        }
        
        private void InitializeProjectorsIfNeeded()
        {
            if (_safeProjector == null)
            {
                var init = _types.GenerateInitialPayload(_projectorName);
                if (!init.IsSuccess)
                {
                    throw init.GetException();
                }
                _safeProjector = init.GetValue();
                _unsafeProjector = CloneProjector(_safeProjector);
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
            allEvents.Sort((a, b) => string.Compare(a.SortableUniqueIdValue, b.SortableUniqueIdValue, StringComparison.Ordinal));
            
            // Rebuild safe state from scratch
            var rebuiltProjector = _types.GenerateInitialPayload(_projectorName);
            if (!rebuiltProjector.IsSuccess)
            {
                throw rebuiltProjector.GetException();
            }
            
            var newSafeProjector = rebuiltProjector.GetValue();
            var newSafeVersion = 0;
            Guid newSafeLastEventId = Guid.Empty;
            string newSafeLastSortableId = string.Empty;
            
            foreach (var ev in allEvents)
            {
                var projected = _types.Project(newSafeProjector, ev);
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
        
        private IMultiProjectorCommon CloneProjector(IMultiProjectorCommon source)
        {
            // Serialize and deserialize to create a deep clone
            var serialized = _types.Serialize(source, _jsonOptions);
            if (!serialized.IsSuccess)
            {
                throw serialized.GetException();
            }
            
            var deserialized = _types.Deserialize(serialized.GetValue(), source.GetType().FullName ?? source.GetType().Name, _jsonOptions);
            if (!deserialized.IsSuccess)
            {
                throw deserialized.GetException();
            }
            
            return deserialized.GetValue();
        }
    }
}