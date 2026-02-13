using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain implementation for tag state management
///     Delegates to GeneralTagStateActor for actual functionality
/// </summary>
public class TagStateGrain : Grain, ITagStateGrain
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly IPersistentState<TagStateCacheState> _cache;
    private readonly IEventTypes _eventTypes;
    private readonly ITagTypes _tagTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;
    private readonly ITagStateProjectionPrimitive _tagStateProjectionPrimitive;
    private readonly IEventStore _eventStore;
    private TagStateId? _tagStateId;

    public TagStateGrain(
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IActorObjectAccessor actorAccessor,
        [PersistentState("tagStateCache", "OrleansStorage")] IPersistentState<TagStateCacheState> cache)
    {
        _eventStore = eventStore;
        if (domainTypes is null)
        {
            throw new ArgumentNullException(nameof(domainTypes));
        }

        _eventTypes = domainTypes.EventTypes;
        _tagTypes = domainTypes.TagTypes;
        _tagProjectorTypes = domainTypes.TagProjectorTypes;
        _tagStatePayloadTypes = domainTypes.TagStatePayloadTypes;
        _tagStateProjectionPrimitive = new NativeTagStateProjectionPrimitive(
            domainTypes.EventTypes,
            domainTypes.TagProjectorTypes,
            domainTypes.TagStatePayloadTypes);
        _actorAccessor = actorAccessor;
        _cache = cache;
    }

    public Task<string> GetTagStateActorIdAsync()
    {
        if (_tagStateId == null)
        {
            return Task.FromResult(string.Empty);
        }

        return Task.FromResult(_tagStateId.GetTagStateId());
    }

    public async Task<SerializableTagState> GetStateAsync()
    {
        if (_tagStateId == null)
        {
            // Return empty serializable state
            return new SerializableTagState(
                Array.Empty<byte>(),
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                nameof(EmptyTagStatePayload),
                string.Empty);
        }

        var latestSortableUniqueId = await GetLatestSortableUniqueIdAsync(_tagStateId);
        var cachedState = _cache.State?.CachedState;

        var projectorVersionResult = _tagProjectorTypes.GetProjectorVersion(_tagStateId.TagProjectorName);
        var projectorVersion = projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : string.Empty;

        if (cachedState != null &&
            cachedState.ProjectorVersion == projectorVersion &&
            !string.IsNullOrEmpty(cachedState.LastSortedUniqueId) &&
            cachedState.LastSortedUniqueId == latestSortableUniqueId)
        {
            return cachedState;
        }

        var usableCachedState = cachedState?.ProjectorVersion == projectorVersion ? cachedState : null;

        var since = ResolveSinceForRead(usableCachedState, projectorVersion, latestSortableUniqueId);
        var eventsResult = await ReadSerializableEventsByTagAsync(
            _tagTypes.GetTag($"{_tagStateId.TagGroup}:{_tagStateId.TagContent}"), since);
        if (!eventsResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to read serialized events: {eventsResult.GetException().Message}",
                eventsResult.GetException());
        }

        var accumulator = _tagStateProjectionPrimitive.CreateAccumulator(_tagStateId);
        if (!accumulator.ApplyState(usableCachedState))
        {
            throw new InvalidOperationException(
                $"Failed to apply cached state for tag state {_tagStateId.GetTagStateId()}");
        }

        if (!accumulator.ApplyEvents(eventsResult.GetValue(), latestSortableUniqueId))
        {
            throw new InvalidOperationException(
                $"Failed to apply events for tag state {_tagStateId.GetTagStateId()}");
        }

        var projectedState = accumulator.GetSerializedState();
        _cache.State = new TagStateCacheState { CachedState = projectedState };
        await _cache.WriteStateAsync();
        return projectedState;
    }

    public async Task<TagState> GetTagStateAsync()
    {
        if (_tagStateId == null)
        {
            // Return empty tag state
            return new TagState(
                new EmptyTagStatePayload(),
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        var serialized = await GetStateAsync();
        if (serialized.TagPayloadName == nameof(EmptyTagStatePayload))
        {
            return new TagState(
                new EmptyTagStatePayload(),
                serialized.Version,
                serialized.LastSortedUniqueId,
                serialized.TagGroup,
                serialized.TagContent,
                serialized.TagProjector,
                serialized.ProjectorVersion);
        }

        var deserializeResult = _tagStatePayloadTypes.DeserializePayload(
            serialized.TagPayloadName,
            serialized.Payload);
        if (!deserializeResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize payload '{serialized.TagPayloadName}': {deserializeResult.GetException().Message}",
                deserializeResult.GetException());
        }

        return new TagState(
            deserializeResult.GetValue(),
            serialized.Version,
            serialized.LastSortedUniqueId,
            serialized.TagGroup,
            serialized.TagContent,
            serialized.TagProjector,
            serialized.ProjectorVersion);
    }

    public async Task UpdateStateAsync(TagState newState)
    {
        if (_tagStateId == null)
        {
            return;
        }

        if (newState.TagGroup != _tagStateId.TagGroup ||
            newState.TagContent != _tagStateId.TagContent ||
            newState.TagProjector != _tagStateId.TagProjectorName)
        {
            throw new InvalidOperationException(
                $"Cannot change tag state identity. Expected {_tagStateId}, but got {newState.TagGroup}:{newState.TagContent}:{newState.TagProjector}");
        }

        SerializableTagState serialized;
        if (newState.Payload is EmptyTagStatePayload)
        {
            serialized = new SerializableTagState(
                Array.Empty<byte>(),
                newState.Version,
                newState.LastSortedUniqueId,
                newState.TagGroup,
                newState.TagContent,
                newState.TagProjector,
                nameof(EmptyTagStatePayload),
                newState.ProjectorVersion);
        }
        else
        {
            var serializeResult = _tagStatePayloadTypes.SerializePayload(newState.Payload);
            if (!serializeResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to serialize payload: {serializeResult.GetException().Message}",
                    serializeResult.GetException());
            }

            serialized = new SerializableTagState(
                serializeResult.GetValue(),
                newState.Version,
                newState.LastSortedUniqueId,
                newState.TagGroup,
                newState.TagContent,
                newState.TagProjector,
                newState.Payload.GetType().Name,
                newState.ProjectorVersion);
        }

        _cache.State = new TagStateCacheState { CachedState = serialized };
        await _cache.WriteStateAsync();
    }

    public async Task ClearCacheAsync()
    {
        if (_tagStateId == null)
        {
            return;
        }

        _cache.State = new TagStateCacheState();
        await _cache.WriteStateAsync();
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Extract tag state ID from grain key
        var tagStateId = ServiceIdGrainKey.Strip(this.GetPrimaryKeyString());
        _tagStateId = TagStateId.Parse(tagStateId);
        return base.OnActivateAsync(cancellationToken);
    }

    private async Task<string?> GetLatestSortableUniqueIdAsync(TagStateId tagStateId)
    {
        var tagConsistentActorId = $"{tagStateId.TagGroup}:{tagStateId.TagContent}";
        var tagConsistentActorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (!tagConsistentActorResult.IsSuccess)
        {
            return null;
        }

        var latestSortableUniqueIdResult = await tagConsistentActorResult.GetValue().GetLatestSortableUniqueIdAsync();
        return latestSortableUniqueIdResult.IsSuccess
            ? latestSortableUniqueIdResult.GetValue()
            : null;
    }

    private static SortableUniqueId? ResolveSinceForRead(
        SerializableTagState? cachedState,
        string projectorVersion,
        string? latestSortableUniqueId)
    {
        if (cachedState == null)
        {
            return null;
        }

        if (cachedState.ProjectorVersion != projectorVersion)
        {
            return null;
        }

        if (string.IsNullOrEmpty(cachedState.LastSortedUniqueId) || string.IsNullOrEmpty(latestSortableUniqueId))
        {
            return null;
        }

        if (!string.Equals(cachedState.LastSortedUniqueId, latestSortableUniqueId, StringComparison.Ordinal) &&
            string.Compare(latestSortableUniqueId, cachedState.LastSortedUniqueId, StringComparison.Ordinal) > 0)
        {
            return SortableUniqueId.TryParse(cachedState.LastSortedUniqueId, out var since)
                ? since
                : null;
        }

        return null;
    }

    private async Task<ResultBox<IReadOnlyList<SerializableEvent>>> ReadSerializableEventsByTagAsync(
        ITag tag,
        SortableUniqueId? since)
    {
        var serializableResult = await _eventStore.ReadSerializableEventsByTagAsync(tag, since);
        if (serializableResult.IsSuccess)
        {
            return ResultBox.FromValue<IReadOnlyList<SerializableEvent>>(serializableResult.GetValue().ToList());
        }

        // Backward-compatible fallback for stores that don't yet support SerializableEvent path.
        var typedResult = await _eventStore.ReadEventsByTagAsync(tag, since);
        if (!typedResult.IsSuccess)
        {
            return ResultBox.Error<IReadOnlyList<SerializableEvent>>(typedResult.GetException());
        }

        var serialized = typedResult
            .GetValue()
            .Select(e => e.ToSerializableEvent(_eventTypes))
            .ToList();
        return ResultBox.FromValue<IReadOnlyList<SerializableEvent>>(serialized);
    }
}
