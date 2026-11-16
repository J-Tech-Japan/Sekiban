using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation for serialized TagState projection primitive.
/// </summary>
public sealed class NativeTagStateProjectionPrimitive : ITagStateProjectionPrimitive
{
    private readonly IEventTypes _eventTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;

    public NativeTagStateProjectionPrimitive(
        IEventTypes eventTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagStatePayloadTypes tagStatePayloadTypes)
    {
        _eventTypes = eventTypes;
        _tagProjectorTypes = tagProjectorTypes;
        _tagStatePayloadTypes = tagStatePayloadTypes;
    }

    public ITagStateProjectionAccumulator CreateAccumulator(TagStateId tagStateId)
    {
        // An unknown projector leaves the accumulator without a projector and with an empty version.
        Func<ITagStatePayload, Event, ITagStatePayload>? projector;
        try
        {
            projector = _tagProjectorTypes.GetProjectorFunction(tagStateId.TagProjectorName);
        }
        catch (Exception)
        {
            projector = null;
        }

        string projectorVersion;
        try
        {
            projectorVersion = _tagProjectorTypes.GetProjectorVersion(tagStateId.TagProjectorName) ?? string.Empty;
        }
        catch (Exception)
        {
            projectorVersion = string.Empty;
        }

        return new NativeTagStateProjectionAccumulator(
            tagStateId,
            _eventTypes,
            _tagStatePayloadTypes,
            projector,
            projectorVersion);
    }

    private sealed class NativeTagStateProjectionAccumulator : ITagStateProjectionAccumulator
    {
        private readonly TagStateId _tagStateId;
        private readonly IEventTypes _eventTypes;
        private readonly ITagStatePayloadTypes _tagStatePayloadTypes;
        private readonly Func<ITagStatePayload, Event, ITagStatePayload>? _projector;
        private readonly string _projectorVersion;
        private readonly string _tagGroup;
        private readonly string _tagContent;
        private readonly string _tagProjector;
        private SerializableTagState? _cachedState;
        private ITagStatePayload _currentPayload = new EmptyTagStatePayload();
        private int _version;
        private string _lastSortableUniqueId = string.Empty;
        private bool _hasProjector;
        private bool _hasChanges;

        public NativeTagStateProjectionAccumulator(
            TagStateId tagStateId,
            IEventTypes eventTypes,
            ITagStatePayloadTypes tagStatePayloadTypes,
            Func<ITagStatePayload, Event, ITagStatePayload>? projector,
            string projectorVersion)
        {
            _tagStateId = tagStateId;
            _eventTypes = eventTypes;
            _tagStatePayloadTypes = tagStatePayloadTypes;
            _projector = projector;
            _projectorVersion = projectorVersion;
            _tagGroup = tagStateId.TagGroup;
            _tagContent = tagStateId.TagContent;
            _tagProjector = tagStateId.TagProjectorName;
            _hasProjector = projector != null;
        }

        public bool ApplyState(SerializableTagState? cachedState)
        {
            _cachedState = cachedState;

            if (_cachedState == null)
            {
                _currentPayload = new EmptyTagStatePayload();
                _version = 0;
                _lastSortableUniqueId = string.Empty;
                return true;
            }

            if (_cachedState.TagProjector != _tagProjector ||
                _cachedState.TagGroup != _tagGroup ||
                _cachedState.TagContent != _tagContent)
            {
                _currentPayload = new EmptyTagStatePayload();
                _version = 0;
                _lastSortableUniqueId = string.Empty;
                return true;
            }

            if (_cachedState.ResolvedPayloadName == nameof(EmptyTagStatePayload))
            {
                _currentPayload = new EmptyTagStatePayload();
                _version = _cachedState.Version;
                _lastSortableUniqueId = _cachedState.LastSortedUniqueId;
                return true;
            }

            ITagStatePayload? payload;
            try
            {
                payload = _tagStatePayloadTypes.DeserializePayload(_cachedState.ResolvedPayloadName, _cachedState.Payload);
            }
            catch (Exception)
            {
                payload = null;
            }

            if (payload is null)
            {
                return false;
            }

            _currentPayload = payload;
            _version = _cachedState.Version;
            _lastSortableUniqueId = _cachedState.LastSortedUniqueId;
            return true;
        }

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_hasProjector)
            {
                _version = 0;
                _lastSortableUniqueId = string.Empty;
                _currentPayload = new EmptyTagStatePayload();
                return true;
            }

            foreach (var serializableEvent in events.OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ApplyEvent(serializableEvent, latestSortableUniqueId, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Native streaming path: deliberately bypasses the batch method's OrderBy because callers validate stream
        ///     order before invoking this member.
        /// </summary>
        public bool ApplyEvent(
            SerializableEvent serializableEvent,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_hasProjector)
            {
                _version = 0;
                _lastSortableUniqueId = string.Empty;
                _currentPayload = new EmptyTagStatePayload();
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(_lastSortableUniqueId) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, _lastSortableUniqueId, StringComparison.Ordinal) <= 0)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(latestSortableUniqueId) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) > 0)
            {
                return true;
            }

            Event? domainEvent;
            try
            {
                domainEvent = serializableEvent.ToEvent(_eventTypes);
            }
            catch (Exception)
            {
                domainEvent = null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (domainEvent is null)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _currentPayload = _projector!(_currentPayload, domainEvent);
            _version++;
            _lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
            _hasChanges = true;
            return true;
        }

        public SerializableTagState GetSerializedState()
        {
            if (_hasProjector is false)
            {
                return SerializableTagStateForEmpty(_tagStateId, _projectorVersion);
            }

            if (!_hasChanges && _cachedState != null)
            {
                return _cachedState;
            }

            (byte[] Bytes, string PayloadName) serialized;
            try
            {
                serialized = SerializePayload(_currentPayload);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize tag state for {_tagStateId.GetTagStateId()}: {ex.Message}",
                    ex);
            }

            return new SerializableTagState(
                serialized.Bytes,
                _version,
                _lastSortableUniqueId,
                _tagGroup,
                _tagContent,
                _tagProjector,
                serialized.PayloadName,
                _projectorVersion,
                serialized.PayloadName);
        }

        public void Dispose()
        {
        }

        private (byte[] Bytes, string PayloadName) SerializePayload(ITagStatePayload payload) =>
            payload is EmptyTagStatePayload
                ? (Array.Empty<byte>(), nameof(EmptyTagStatePayload))
                : (_tagStatePayloadTypes.SerializePayload(payload), payload.GetType().Name);
    }

    private static SerializableTagState SerializableTagStateForEmpty(TagStateId tagStateId, string projectorVersion) =>
        new(
            Array.Empty<byte>(),
            0,
            string.Empty,
            tagStateId.TagGroup,
            tagStateId.TagContent,
            tagStateId.TagProjectorName,
            nameof(EmptyTagStatePayload),
            projectorVersion,
            nameof(EmptyTagStatePayload));
}
