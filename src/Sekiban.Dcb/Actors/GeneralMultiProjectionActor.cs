using System;
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
        
        // Buffer for events within SafeWindow
        private readonly List<Event> _bufferedEvents = new();

        public GeneralMultiProjectionActor(DcbDomainTypes domainTypes, string projectorName, GeneralMultiProjectionActorOptions? options = null)
        {
            _types = domainTypes.MultiProjectorTypes;
            _domain = domainTypes;
            _jsonOptions = domainTypes.JsonSerializerOptions;
            _projectorName = projectorName;
            _options = options ?? new GeneralMultiProjectionActorOptions();
        }

        public async Task AddEventsAsync(System.Collections.Generic.IReadOnlyList<Event> events)
        {
            // Initialize projectors if needed
            InitializeProjectorsIfNeeded();

            var safeWindowThreshold = GetSafeWindowThreshold();
            
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
                    // Process to safe state directly
                    var safeProjected = _types.Project(_safeProjector!, ev);
                    if (!safeProjected.IsSuccess)
                    {
                        throw safeProjected.GetException();
                    }
                    _safeProjector = safeProjected.GetValue();
                    _safeLastEventId = ev.Id;
                    _safeLastSortableUniqueId = ev.SortableUniqueIdValue;
                    _safeVersion++;
                }
                else
                {
                    // Buffer event for later processing
                    _bufferedEvents.Add(ev);
                }
            }
            
            // Process buffered events that are now outside safe window
            await ProcessBufferedEventsAsync();
        }

        public async Task AddSerializableEventsAsync(System.Collections.Generic.IReadOnlyList<SerializableEvent> events)
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
            await AddEventsAsync(list);
        }

        public Task<ResultBox<MultiProjectionState>> GetStateAsync()
        {
            InitializeProjectorsIfNeeded();

            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_safeProjector!);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<MultiProjectionState>(projectorNameRb.GetException()));
            }

            // Return safe state
            var state = new MultiProjectionState(
                (IMultiProjectionPayload)_safeProjector!,
                projectorNameRb.GetValue(),
                _safeProjector!.GetVersion(),
                _safeLastSortableUniqueId,
                _safeLastEventId,
                _safeVersion
            );
            return Task.FromResult(ResultBox.FromValue(state));
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
            
            // Clear buffered events when loading state
            _bufferedEvents.Clear();
            
            return Task.CompletedTask;
        }

        public Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync()
        {
            InitializeProjectorsIfNeeded();

            // Resolve projector name and serialize safe state payload
            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_safeProjector!);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(projectorNameRb.GetException()));
            }
            var name = projectorNameRb.GetValue();

            var payloadBytesRb = _types.Serialize(_safeProjector!, _jsonOptions);
            if (!payloadBytesRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(payloadBytesRb.GetException()));
            }

            var state = new SerializableMultiProjectionState(
                payloadBytesRb.GetValue(),
                _safeProjector!.GetType().FullName ?? _safeProjector!.GetType().Name,
                name,
                _safeProjector!.GetVersion(),
                _safeLastSortableUniqueId,
                _safeLastEventId,
                _safeVersion);

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
                _unsafeVersion
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
        
        private async Task ProcessBufferedEventsAsync()
        {
            var safeWindowThreshold = GetSafeWindowThreshold();
            var eventsToProcess = new List<Event>();
            
            // Find events that are now outside safe window
            for (int i = _bufferedEvents.Count - 1; i >= 0; i--)
            {
                var ev = _bufferedEvents[i];
                var eventTime = new SortableUniqueId(ev.SortableUniqueIdValue);
                
                if (eventTime.IsEarlierThanOrEqual(safeWindowThreshold))
                {
                    eventsToProcess.Add(ev);
                    _bufferedEvents.RemoveAt(i);
                }
            }
            
            // Sort events by SortableUniqueId
            eventsToProcess.Sort((a, b) => string.Compare(a.SortableUniqueIdValue, b.SortableUniqueIdValue, StringComparison.Ordinal));
            
            // Rebuild safe state from last safe point with buffered events
            if (eventsToProcess.Count > 0)
            {
                // Clone current safe state as starting point
                var rebuiltProjector = CloneProjector(_safeProjector!);
                
                // Apply sorted events
                foreach (var ev in eventsToProcess)
                {
                    var projected = _types.Project(rebuiltProjector, ev);
                    if (!projected.IsSuccess)
                    {
                        throw projected.GetException();
                    }
                    rebuiltProjector = projected.GetValue();
                    _safeLastEventId = ev.Id;
                    _safeLastSortableUniqueId = ev.SortableUniqueIdValue;
                    _safeVersion++;
                }
                
                _safeProjector = rebuiltProjector;
            }
            
            await Task.CompletedTask;
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