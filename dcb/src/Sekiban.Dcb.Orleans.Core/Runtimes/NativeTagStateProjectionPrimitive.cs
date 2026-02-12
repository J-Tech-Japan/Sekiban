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

    public Task<ResultBox<SerializableTagState>> ProjectAsync(
        TagStateProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectorFuncResult = _tagProjectorTypes.GetProjectorFunction(request.TagStateId.TagProjectorName);
            if (!projectorFuncResult.IsSuccess)
            {
                return Task.FromResult(
                    ResultBox.FromValue(
                        SerializableTagStateForEmpty(
                            request.TagStateId,
                            projectorVersion: string.Empty)));
            }

            var projectorVersionResult = _tagProjectorTypes.GetProjectorVersion(request.TagStateId.TagProjectorName);
            var projectorVersion = projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : string.Empty;

            if (string.IsNullOrEmpty(request.LatestSortableUniqueId))
            {
                return Task.FromResult(
                    ResultBox.FromValue(
                        SerializableTagStateForEmpty(
                            request.TagStateId,
                            projectorVersion)));
            }

            var projector = projectorFuncResult.GetValue();
            var events = request.Events
                .OrderBy(e => e.SortableUniqueIdValue, StringComparer.Ordinal)
                .ToList();

            var cachedState = TryDeserializeCachedState(request.CachedState);
            var canIncrementalUpdate = cachedState != null &&
                cachedState.ProjectorVersion == projectorVersion &&
                !string.IsNullOrEmpty(cachedState.LastSortedUniqueId) &&
                string.Compare(
                    request.LatestSortableUniqueId,
                    cachedState.LastSortedUniqueId,
                    StringComparison.Ordinal) > 0;

            ITagStatePayload currentState;
            var version = 0;
            var lastSortedUniqueId = string.Empty;

            if (canIncrementalUpdate)
            {
                currentState = cachedState!.Payload;
                version = cachedState.Version;
                lastSortedUniqueId = cachedState.LastSortedUniqueId;
                events = events
                    .Where(e =>
                        string.Compare(e.SortableUniqueIdValue, cachedState.LastSortedUniqueId, StringComparison.Ordinal) > 0 &&
                        string.Compare(e.SortableUniqueIdValue, request.LatestSortableUniqueId, StringComparison.Ordinal) <= 0)
                    .ToList();
            }
            else
            {
                currentState = new EmptyTagStatePayload();
                events = events
                    .Where(e =>
                        string.Compare(e.SortableUniqueIdValue, request.LatestSortableUniqueId, StringComparison.Ordinal) <= 0)
                    .ToList();
            }

            foreach (var serializableEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var eventResult = serializableEvent.ToEvent(_eventTypes);
                if (!eventResult.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<SerializableTagState>(eventResult.GetException()));
                }

                var eventData = eventResult.GetValue();
                currentState = projector(currentState, eventData);
                version++;
                lastSortedUniqueId = eventData.SortableUniqueIdValue;
            }

            var serializationResult = SerializePayload(currentState);
            if (!serializationResult.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableTagState>(serializationResult.GetException()));
            }

            var projectedState = new SerializableTagState(
                serializationResult.GetValue().Bytes,
                version,
                lastSortedUniqueId,
                request.TagStateId.TagGroup,
                request.TagStateId.TagContent,
                request.TagStateId.TagProjectorName,
                serializationResult.GetValue().PayloadName,
                projectorVersion);

            return Task.FromResult(ResultBox.FromValue(projectedState));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<SerializableTagState>(ex));
        }
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

    private TagState? TryDeserializeCachedState(SerializableTagState? cachedState)
    {
        if (cachedState == null)
        {
            return null;
        }

        if (cachedState.TagPayloadName == nameof(EmptyTagStatePayload))
        {
            return new TagState(
                new EmptyTagStatePayload(),
                cachedState.Version,
                cachedState.LastSortedUniqueId,
                cachedState.TagGroup,
                cachedState.TagContent,
                cachedState.TagProjector,
                cachedState.ProjectorVersion);
        }

        var deserializeResult = _tagStatePayloadTypes.DeserializePayload(cachedState.TagPayloadName, cachedState.Payload);
        if (!deserializeResult.IsSuccess)
        {
            return null;
        }

        return new TagState(
            deserializeResult.GetValue(),
            cachedState.Version,
            cachedState.LastSortedUniqueId,
            cachedState.TagGroup,
            cachedState.TagContent,
            cachedState.TagProjector,
            cachedState.ProjectorVersion);
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
