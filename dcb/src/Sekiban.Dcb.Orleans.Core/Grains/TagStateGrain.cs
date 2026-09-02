using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Runtime;
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
    private readonly ITagTypes _tagTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;
    private readonly ITagStateProjectionPrimitive _tagStateProjectionPrimitive;
    private readonly IEventStore _eventStore;
    private TagStateId? _tagStateId;

    public TagStateGrain(
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        ITagStateProjectionPrimitive tagStateProjectionPrimitive,
        IActorObjectAccessor actorAccessor,
        [PersistentState("tagStateCache", "OrleansStorage")] IPersistentState<TagStateCacheState> cache)
    {
        _eventStore = eventStore;
        if (domainTypes is null)
        {
            throw new ArgumentNullException(nameof(domainTypes));
        }

        _tagTypes = domainTypes.TagTypes;
        _tagProjectorTypes = domainTypes.TagProjectorTypes;
        _tagStatePayloadTypes = domainTypes.TagStatePayloadTypes;
        _tagStateProjectionPrimitive = tagStateProjectionPrimitive ?? throw new ArgumentNullException(nameof(tagStateProjectionPrimitive));
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

    public Task<SerializableTagState> GetStateAsync() => GetStateAsync(CancellationToken.None);

    public async Task<SerializableTagState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                string.Empty,
                nameof(EmptyTagStatePayload));
        }

        var latestSortableUniqueId = await GetLatestSortableUniqueIdAsync(_tagStateId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
        var tag = _tagTypes.GetTag($"{_tagStateId.TagGroup}:{_tagStateId.TagContent}");
        var taggedStream = SekibanDcbCapabilityResolver.ResolveTaggedStream(_eventStore, "event store");

        using var accumulator = await _tagStateProjectionPrimitive.CreateAccumulatorAsync(_tagStateId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accumulator.ApplyState(usableCachedState))
        {
            throw new InvalidOperationException(
                $"Failed to apply cached state for tag state {_tagStateId.GetTagStateId()}");
        }

        if (taggedStream.IsSupported)
        {
            var previousId = since?.Value;
            var until = string.IsNullOrEmpty(latestSortableUniqueId)
                ? null
                : new SortableUniqueId(latestSortableUniqueId);
            var streamResult = await taggedStream.StreamStore!.StreamSerializableEventsByTagAsync(
                tag,
                since,
                until,
                serializableEvent =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!SekibanDcbCapabilityResolver.IsTaggedStreamOrderValid(
                            previousId,
                            serializableEvent.SortableUniqueIdValue,
                            out var duplicate))
                    {
                        throw new InvalidOperationException(
                            $"Tagged stream for {tag.GetTag()} emitted out-of-order id " +
                            $"{serializableEvent.SortableUniqueIdValue} after {previousId}.");
                    }

                    if (duplicate)
                    {
                        return ValueTask.CompletedTask;
                    }

                    if (!string.IsNullOrEmpty(latestSortableUniqueId) &&
                        string.Compare(
                            serializableEvent.SortableUniqueIdValue,
                            latestSortableUniqueId,
                            StringComparison.Ordinal) > 0)
                    {
                        throw new InvalidOperationException(
                            $"Tagged stream for {tag.GetTag()} exceeded captured head {latestSortableUniqueId}.");
                    }

                    if (!accumulator.ApplyEvent(serializableEvent, latestSortableUniqueId, cancellationToken))
                    {
                        throw new InvalidOperationException(
                            $"Failed to apply streamed event for tag state {_tagStateId.GetTagStateId()}");
                    }

                    previousId = serializableEvent.SortableUniqueIdValue;
                    return ValueTask.CompletedTask;
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!streamResult.IsSuccess)
            {
                if (streamResult.GetException() is OperationCanceledException cancellationException)
                {
                    throw cancellationException;
                }

                throw new InvalidOperationException(
                    $"Failed to stream serialized events: {streamResult.GetException().Message}",
                    streamResult.GetException());
            }
        }
        else
        {
            var eventsResult = await ReadSerializableEventsByTagAsync(tag, since, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!eventsResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to read serialized events: {eventsResult.GetException().Message}",
                    eventsResult.GetException());
            }

            if (!accumulator.ApplyEvents(eventsResult.GetValue(), latestSortableUniqueId, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Failed to apply events for tag state {_tagStateId.GetTagStateId()}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var projectedState = accumulator.GetSerializedState();
        _cache.State = new TagStateCacheState { CachedState = projectedState };
        await _cache.WriteStateAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return projectedState;
    }

    public Task<TagState> GetTagStateAsync() => GetTagStateAsync(CancellationToken.None);

    public async Task<TagState> GetTagStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        var serialized = await GetStateAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (serialized.ResolvedPayloadName == nameof(EmptyTagStatePayload))
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
            serialized.ResolvedPayloadName,
            serialized.Payload);
        if (!deserializeResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize payload '{serialized.ResolvedPayloadName}': {deserializeResult.GetException().Message}",
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
                newState.ProjectorVersion,
                nameof(EmptyTagStatePayload));
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
                newState.ProjectorVersion,
                newState.Payload.GetType().Name);
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

    private async Task<string?> GetLatestSortableUniqueIdAsync(
        TagStateId tagStateId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tagConsistentActorId = $"{tagStateId.TagGroup}:{tagStateId.TagContent}";
        var tagConsistentActorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!tagConsistentActorResult.IsSuccess)
        {
            return null;
        }

        var latestSortableUniqueIdResult = await tagConsistentActorResult.GetValue().GetLatestSortableUniqueIdAsync();
        cancellationToken.ThrowIfCancellationRequested();
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
        SortableUniqueId? since,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serializableResult = await _eventStore.ReadSerializableEventsByTagAsync(tag, since);
        cancellationToken.ThrowIfCancellationRequested();
        if (serializableResult.IsSuccess)
        {
            return ResultBox.FromValue<IReadOnlyList<SerializableEvent>>(serializableResult.GetValue().ToList());
        }

        return ResultBox.Error<IReadOnlyList<SerializableEvent>>(serializableResult.GetException());
    }
}
