using ResultBoxes;
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
        var projectorFuncResult = _tagProjectorTypes.GetProjectorFunction(tagStateId.TagProjectorName);
        var projectorVersionResult = _tagProjectorTypes.GetProjectorVersion(tagStateId.TagProjectorName);

        return new NativeTagStateProjectionAccumulator(
            tagStateId,
            _eventTypes,
            _tagStatePayloadTypes,
            projectorFuncResult.IsSuccess ? projectorFuncResult.GetValue() : null,
            projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : string.Empty);
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

            if (_cachedState.TagPayloadName == nameof(EmptyTagStatePayload))
            {
                _currentPayload = new EmptyTagStatePayload();
                _version = _cachedState.Version;
                _lastSortableUniqueId = _cachedState.LastSortedUniqueId;
                return true;
            }

            var deserializeResult = _tagStatePayloadTypes.DeserializePayload(_cachedState.TagPayloadName, _cachedState.Payload);
            if (!deserializeResult.IsSuccess)
            {
                return false;
            }

            _currentPayload = deserializeResult.GetValue();
            _version = _cachedState.Version;
            _lastSortableUniqueId = _cachedState.LastSortedUniqueId;
            return true;
        }

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
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
                if (!string.IsNullOrEmpty(_lastSortableUniqueId) &&
                    string.Compare(serializableEvent.SortableUniqueIdValue, _lastSortableUniqueId, StringComparison.Ordinal) <= 0)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(latestSortableUniqueId) &&
                    string.Compare(serializableEvent.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) > 0)
                {
                    continue;
                }

                var eventResult = serializableEvent.ToEvent(_eventTypes);
                if (!eventResult.IsSuccess)
                {
                    return false;
                }

                _currentPayload = _projector!(_currentPayload, eventResult.GetValue());
                _version++;
                _lastSortableUniqueId = serializableEvent.SortableUniqueIdValue;
                _hasChanges = true;
            }

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

            var serializationResult = SerializePayload(_currentPayload);
            if (!serializationResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize tag state for {_tagStateId.GetTagStateId()}: {serializationResult.GetException().Message}",
                    serializationResult.GetException());
            }

            return new SerializableTagState(
                serializationResult.GetValue().Bytes,
                _version,
                _lastSortableUniqueId,
                _tagGroup,
                _tagContent,
                _tagProjector,
                serializationResult.GetValue().PayloadName,
                _projectorVersion);
        }

        private ResultBox<(byte[] Bytes, string PayloadName)> SerializePayload(ITagStatePayload payload)
        {
            if (payload is EmptyTagStatePayload)
            {
                return ResultBox.FromValue((Array.Empty<byte>(), nameof(EmptyTagStatePayload)));
            }

            var serializeResult = _tagStatePayloadTypes.SerializePayload(payload);
            if (!serializeResult.IsSuccess)
            {
                return ResultBox.Error<(byte[] Bytes, string PayloadName)>(serializeResult.GetException());
            }

            return ResultBox.FromValue((serializeResult.GetValue(), payload.GetType().Name));
        }
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
            projectorVersion);
}
