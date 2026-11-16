using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General implementation of ITagStateActorCommon
///     Computes tag state by reading events and projecting them
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralTagStateActor : ITagStateActorCommon
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly IEventTypes _eventTypes;
    private readonly ITagProjectorTypes _tagProjectorTypes;
    private readonly ITagTypes _tagTypes;
    private readonly ITagStatePayloadTypes _tagStatePayloadTypes;
    private readonly IEventStore _eventStore;
    private readonly TagStateOptions _options;
    private readonly ITagStatePersistent _statePersistent;
    private readonly TagStateId _tagStateId;
    private readonly ILogger<GeneralTagStateActor> _logger;

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        ResolveEventTypes(eventStore, actorAccessor),
        tagProjectorTypes,
        tagTypes,
        tagStatePayloadTypes,
        actorAccessor,
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        eventTypes,
        tagProjectorTypes,
        tagTypes,
        tagStatePayloadTypes,
        new TagStateOptions(),
        actorAccessor,
        new InMemoryTagStatePersistent(),
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        ResolveEventTypes(eventStore, actorAccessor),
        tagProjectorTypes,
        tagTypes,
        tagStatePayloadTypes,
        options,
        actorAccessor,
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        eventTypes,
        tagProjectorTypes,
        tagTypes,
        tagStatePayloadTypes,
        options,
        actorAccessor,
        new InMemoryTagStatePersistent(),
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent,
        ILogger<GeneralTagStateActor>? logger = null) : this(
        tagStateId,
        eventStore,
        ResolveEventTypes(eventStore, actorAccessor),
        tagProjectorTypes,
        tagTypes,
        tagStatePayloadTypes,
        options,
        actorAccessor,
        statePersistent,
        logger)
    {
    }

    public GeneralTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        IEventTypes eventTypes,
        ITagProjectorTypes tagProjectorTypes,
        ITagTypes tagTypes,
        ITagStatePayloadTypes tagStatePayloadTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent,
        ILogger<GeneralTagStateActor>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(tagStateId))
            throw new ArgumentNullException(nameof(tagStateId));

        _tagStateId = TagStateId.Parse(tagStateId);
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _tagProjectorTypes = tagProjectorTypes ?? throw new ArgumentNullException(nameof(tagProjectorTypes));
        _tagTypes = tagTypes ?? throw new ArgumentNullException(nameof(tagTypes));
        _tagStatePayloadTypes = tagStatePayloadTypes ?? throw new ArgumentNullException(nameof(tagStatePayloadTypes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _statePersistent = statePersistent ?? throw new ArgumentNullException(nameof(statePersistent));
        _logger = logger ?? NullLogger<GeneralTagStateActor>.Instance;
    }

    private static IEventTypes ResolveEventTypes(IEventStore eventStore, IActorObjectAccessor actorAccessor)
    {
        // Core still type-checks the volatile test store here. That coupling is exactly why the type
        // cannot physically move out of this package before the next major version; the [Obsolete]
        // pointer to Sekiban.Dcb.Core.Testing is the part that can ship now.
#pragma warning disable CS0618
        if (eventStore is InMemoryEventStore inMemoryEventStore && inMemoryEventStore.EventTypes is not null)
#pragma warning restore CS0618
        {
            return inMemoryEventStore.EventTypes;
        }

        if (actorAccessor is IServiceProvider serviceProvider)
        {
            if (serviceProvider.GetService(typeof(IEventTypes)) is IEventTypes eventTypes)
            {
                return eventTypes;
            }

            if (serviceProvider.GetService(typeof(DcbDomainTypes)) is DcbDomainTypes domainTypes)
            {
                return domainTypes.EventTypes;
            }
        }

        return new ReflectionEventTypes();
    }

    public Task<string> GetTagStateActorIdAsync() => Task.FromResult(_tagStateId.GetTagStateId());

    public Task<SerializableTagState> GetStateAsync() => GetStateAsync(CancellationToken.None);

    public async Task<SerializableTagState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_statePersistent is not ISerializableTagStatePersistent)
        {
            var typedState = await GetTypedTagStateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return SerializeTagState(typedState);
        }

        var serializableState = await GetSerializableStateAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return serializableState;
    }

    public Task<TagState> GetTagStateAsync() => GetTagStateAsync(CancellationToken.None);

    public async Task<TagState> GetTagStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_statePersistent is not ISerializableTagStatePersistent)
        {
            var typedState = await GetTypedTagStateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return typedState;
        }

        var serializableState = await GetSerializableStateAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return DeserializeTagState(serializableState);
    }

    public async Task UpdateStateAsync(TagState newState)
    {
        // Validate that the identity hasn't changed
        if (newState.TagGroup != _tagStateId.TagGroup ||
            newState.TagContent != _tagStateId.TagContent ||
            newState.TagProjector != _tagStateId.TagProjectorName)
        {
            throw new InvalidOperationException(
                $"Cannot change tag state identity. Expected {_tagStateId}, " +
                $"but got {newState.TagGroup}:{newState.TagContent}:{newState.TagProjector}");
        }

        if (_statePersistent is ISerializableTagStatePersistent serializablePersistent)
        {
            await serializablePersistent.SaveSerializableStateAsync(SerializeTagState(newState));
            return;
        }

        await _statePersistent.SaveStateAsync(newState);
    }

    // A failed lookup or read task leaves the latest position unknown (null), exactly as a failed result did before.
    // The tasks are created by the caller, so a call that throws before returning its task propagates.
    private static async Task<ITagConsistentActorCommon?> TryGetTagConsistentActorAsync(
        Task<ITagConsistentActorCommon> actorLookup)
    {
        try
        {
            return await actorLookup;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<string?> TryGetLatestSortableUniqueIdAsync(Task<string> latestRead)
    {
        try
        {
            return await latestRead;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<SerializableTagState> GetSerializableStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectorVersion = GetCurrentProjectorVersion();

        // Always get the latest sortable unique ID from TagConsistentActor
        string? currentLatestSortableUniqueId = null;
        var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
        var tagConsistentActor = await TryGetTagConsistentActorAsync(
            _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId));
        cancellationToken.ThrowIfCancellationRequested();
        if (tagConsistentActor is not null)
        {
            var latestSortableUniqueId = await TryGetLatestSortableUniqueIdAsync(
                tagConsistentActor.GetLatestSortableUniqueIdAsync());
            cancellationToken.ThrowIfCancellationRequested();
            if (latestSortableUniqueId is not null)
            {
                currentLatestSortableUniqueId = latestSortableUniqueId;
            }
        }

        TagState? cachedState = null;
        if (_statePersistent is ISerializableTagStatePersistent serializablePersistent)
        {
            var cachedSerializableState = await serializablePersistent.LoadSerializableStateAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (cachedSerializableState != null &&
                cachedSerializableState.ProjectorVersion == projectorVersion &&
                cachedSerializableState.LastSortedUniqueId == currentLatestSortableUniqueId)
            {
                return cachedSerializableState;
            }

            if (cachedSerializableState != null && cachedSerializableState.ProjectorVersion == projectorVersion)
            {
                cachedState = DeserializeTagState(cachedSerializableState);
            }
        }
        else
        {
            cachedState = await _statePersistent.LoadStateAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (
                cachedState != null &&
                cachedState.ProjectorVersion == projectorVersion &&
                cachedState.LastSortedUniqueId == currentLatestSortableUniqueId)
            {
                return SerializeTagState(cachedState);
            }
        }

        // Cache is stale or doesn't exist, compute new state.
        // Pass the latest sortable unique ID and cached state to avoid duplicate calls.
        var computedState = await ComputeStateFromEventsAsync(currentLatestSortableUniqueId, cachedState, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var computedSerializableState = SerializeTagState(computedState);

        if (_statePersistent is ISerializableTagStatePersistent serializableStatePersistent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await serializableStatePersistent.SaveSerializableStateAsync(computedSerializableState);
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _statePersistent.SaveStateAsync(computedState);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return computedSerializableState;
    }

    private async Task<TagState> GetTypedTagStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var projectorVersion = GetCurrentProjectorVersion();

        // Always get the latest sortable unique ID from TagConsistentActor
        string? currentLatestSortableUniqueId = null;
        var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
        var tagConsistentActor = await TryGetTagConsistentActorAsync(
            _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId));
        cancellationToken.ThrowIfCancellationRequested();
        if (tagConsistentActor is not null)
        {
            var latestSortableUniqueId = await TryGetLatestSortableUniqueIdAsync(
                tagConsistentActor.GetLatestSortableUniqueIdAsync());
            cancellationToken.ThrowIfCancellationRequested();
            if (latestSortableUniqueId is not null)
            {
                currentLatestSortableUniqueId = latestSortableUniqueId;
            }
        }

        // Check if we have cached state
        var cachedState = await _statePersistent.LoadStateAsync();
        cancellationToken.ThrowIfCancellationRequested();

        // If cached state exists and is up-to-date, return it
        if (
            cachedState != null &&
            cachedState.ProjectorVersion == projectorVersion &&
            cachedState.LastSortedUniqueId == currentLatestSortableUniqueId)
        {
            return cachedState;
        }

        // Cache is stale or doesn't exist, compute new state
        // Pass the latest sortable unique ID and cached state to avoid duplicate calls
        var computedState = await ComputeStateFromEventsAsync(currentLatestSortableUniqueId, cachedState, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Save the newly computed state to cache
        cancellationToken.ThrowIfCancellationRequested();
        await _statePersistent.SaveStateAsync(computedState);

        cancellationToken.ThrowIfCancellationRequested();
        return computedState;
    }

    private SerializableTagState SerializeTagState(TagState tagState)
    {
        if (tagState.Payload is EmptyTagStatePayload)
        {
            return new SerializableTagState(
                Array.Empty<byte>(),
                tagState.Version,
                tagState.LastSortedUniqueId,
                tagState.TagGroup,
                tagState.TagContent,
                tagState.TagProjector,
                "EmptyTagStatePayload",
                tagState.ProjectorVersion,
                nameof(EmptyTagStatePayload));
        }

        // Use ITagStatePayloadTypes for serialization
        byte[] serializedPayload;
        try
        {
            serializedPayload = _tagStatePayloadTypes.SerializePayload(tagState.Payload);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to serialize payload: {ex.Message}");
        }

        return new SerializableTagState(
            serializedPayload,
            tagState.Version,
            tagState.LastSortedUniqueId,
            tagState.TagGroup,
            tagState.TagContent,
            tagState.TagProjector,
            tagState.Payload.GetType().Name,
            tagState.ProjectorVersion,
            tagState.Payload.GetType().Name);
    }

    private TagState DeserializeTagState(SerializableTagState state)
    {
        if (state.ResolvedPayloadName == nameof(EmptyTagStatePayload))
        {
            return new TagState(
                new EmptyTagStatePayload(),
                state.Version,
                state.LastSortedUniqueId,
                state.TagGroup,
                state.TagContent,
                state.TagProjector,
                state.ProjectorVersion);
        }

        ITagStatePayload payload;
        try
        {
            payload = _tagStatePayloadTypes.DeserializePayload(state.ResolvedPayloadName, state.Payload);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize payload '{state.ResolvedPayloadName}': {ex.Message}");
        }

        return new TagState(
            payload,
            state.Version,
            state.LastSortedUniqueId,
            state.TagGroup,
            state.TagContent,
            state.TagProjector,
            state.ProjectorVersion);
    }

    private string GetCurrentProjectorVersion() => TryGetProjectorVersion() ?? string.Empty;

    private string? TryGetProjectorVersion()
    {
        try
        {
            return _tagProjectorTypes.GetProjectorVersion(_tagStateId.TagProjectorName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<TagState> ComputeStateFromEventsAsync(
        string? latestSortableUniqueId,
        TagState? cachedState,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Get the projector function
        Func<ITagStatePayload, Event, ITagStatePayload> projectFunc;
        try
        {
            projectFunc = _tagProjectorTypes.GetProjectorFunction(_tagStateId.TagProjectorName);
        }
        catch (Exception)
        {
            // Return empty state if projector not found
            return TagState.GetEmpty(_tagStateId);
        }

        // Get the projector version
        var projectorVersion = GetCurrentProjectorVersion();

        // Create the tag to query events
        var tag = CreateTag(_tagStateId.TagGroup, _tagStateId.TagContent);

        // If TagConsistentActor has no LastSortableUniqueId, return empty state
        if (string.IsNullOrEmpty(latestSortableUniqueId))
        {
            return TagState.GetEmpty(_tagStateId) with { ProjectorVersion = projectorVersion };
        }

        ITagStatePayload? currentState = null;
        var version = 0;
        var lastSortedUniqueId = "";
        var taggedStream = SekibanDcbCapabilityResolver.ResolveTaggedStream(_eventStore, "event store");

        // Try incremental update if possible
        if (cachedState is { } incrementalCachedState &&
            incrementalCachedState.ProjectorVersion == projectorVersion &&
            !string.IsNullOrEmpty(incrementalCachedState.LastSortedUniqueId) &&
            !string.IsNullOrEmpty(latestSortableUniqueId) &&
            string.Compare(latestSortableUniqueId, incrementalCachedState.LastSortedUniqueId, StringComparison.Ordinal) > 0)
        {
            // Use cached state as starting point
            currentState = incrementalCachedState.Payload;
            version = incrementalCachedState.Version;
            lastSortedUniqueId = incrementalCachedState.LastSortedUniqueId;

            // Read only new events after the cached state's last sortable unique ID.
            // The event store supports a "since" parameter so the database returns only
            // delta rows instead of loading the full event history for the tag.
            // We can wrap the raw string directly because LastSortedUniqueId always
            // originates from an event's SortableUniqueId and the branch above
            // guarantees it is not null or empty.
            var since = new SortableUniqueId(incrementalCachedState.LastSortedUniqueId);
            if (taggedStream.IsSupported)
            {
                var (streamProjection, error) = await TaggedStreamProjectionHelper.ProjectAsync(
                    taggedStream.StreamStore!,
                    tag,
                    since,
                    new SortableUniqueId(latestSortableUniqueId),
                    _eventTypes,
                    projectFunc,
                    incrementalCachedState.Payload,
                    lastSortedUniqueId,
                    lastSortedUniqueId,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (error is not null)
                {
                    _logger.LogError(error, "[GeneralTagStateActor] Error streaming events for tag {Tag}", tag.GetTag());
                    throw new InvalidOperationException($"Failed to stream events for tag {tag.GetTag()}: {error.Message}", error);
                }

                currentState = streamProjection!.State;
                version += streamProjection.EventCount;
                lastSortedUniqueId = streamProjection.LastSortableUniqueId;
            }
            else
            {
                var eventsRead = _eventStore.ReadEventsByTagAsync(tag, _eventTypes, since);
                IEnumerable<Event> readEvents;
                try
                {
                    readEvents = await eventsRead;
                }
                catch (Exception error)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Log the error and throw exception instead of silently returning cached state
                    _logger.LogError(
                        error,
                        "[GeneralTagStateActor] Error reading events for tag {Tag}",
                        tag.GetTag());

                    // For deserialization errors, we should not use cached state as it may be inconsistent
                    // Instead, throw the error so developers can see and fix the issue
                    throw new InvalidOperationException(
                        $"Failed to read events for tag {tag.GetTag()}: {error.Message}",
                        error);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // The DB already filtered out events <= cachedState.LastSortedUniqueId.
                // We still need the upper-bound filter to stay consistent with the
                // latestSortableUniqueId snapshot taken from TagConsistentActor.
                var newEvents = readEvents
                    .Where(e =>
                        string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <= 0)
                    .ToList();

                // Project only the new events on top of cached state
                foreach (var evt in newEvents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentState = projectFunc(currentState, evt);
                    cancellationToken.ThrowIfCancellationRequested();
                    version++;
                    lastSortedUniqueId = evt.SortableUniqueIdValue;
                }
            }
        }
        else
        {
            // Full rebuild: projector version changed or no valid cache
            if (taggedStream.IsSupported)
            {
                var (streamProjection, error) = await TaggedStreamProjectionHelper.ProjectAsync(
                    taggedStream.StreamStore!,
                    tag,
                    null,
                    new SortableUniqueId(latestSortableUniqueId),
                    _eventTypes,
                    projectFunc,
                    new EmptyTagStatePayload(),
                    null,
                    string.Empty,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (error is not null)
                {
                    _logger.LogError(
                        error,
                        "[GeneralTagStateActor] Error streaming events for full rebuild of tag {Tag}",
                        tag.GetTag());
                    throw new InvalidOperationException(
                        $"Failed to stream events for tag {tag.GetTag()} during full rebuild: {error.Message}",
                        error);
                }

                currentState = streamProjection!.State;
                version = streamProjection.EventCount;
                lastSortedUniqueId = streamProjection.LastSortableUniqueId;
            }
            else
            {
                var eventsRead = _eventStore.ReadEventsByTagAsync(tag, _eventTypes);
                IEnumerable<Event> readEvents;
                try
                {
                    readEvents = await eventsRead;
                }
                catch (Exception error)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Log the error for debugging
                    _logger.LogError(
                        error,
                        "[GeneralTagStateActor] Error reading events for full rebuild of tag {Tag}",
                        tag.GetTag());

                    // For full rebuild, if we can't read events, throw the error
                    // This ensures developers see the issue (like missing event type registration)
                    throw new InvalidOperationException(
                        $"Failed to read events for tag {tag.GetTag()} during full rebuild: {error.Message}",
                        error);
                }

                cancellationToken.ThrowIfCancellationRequested();

                var events = readEvents
                    .Where(e => string.Compare(e.SortableUniqueIdValue, latestSortableUniqueId, StringComparison.Ordinal) <=
                        0)
                    .ToList();

                // Project all events from scratch
                currentState = null;
                version = 0;
                lastSortedUniqueId = "";

                foreach (var evt in events)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Initialize state with EmptyTagStatePayload if needed
                    currentState ??= new EmptyTagStatePayload();

                    // Project the event
                    currentState = projectFunc(currentState, evt);
                    cancellationToken.ThrowIfCancellationRequested();
                    version++;

                    // Keep track of the last sortable unique id
                    if (!string.IsNullOrEmpty(evt.SortableUniqueIdValue))
                    {
                        lastSortedUniqueId = evt.SortableUniqueIdValue;
                    }
                }
            }
        }

        // If no events were processed, ensure we have at least EmptyTagStatePayload
        cancellationToken.ThrowIfCancellationRequested();
        currentState ??= new EmptyTagStatePayload();

        return new TagState(
            currentState,
            version,
            lastSortedUniqueId,
            _tagStateId.TagGroup,
            _tagStateId.TagContent,
            _tagStateId.TagProjectorName,
            projectorVersion);
    }

    private ITag CreateTag(string tagGroup, string tagContent)
    {
        // Create tag string in "group:content" format
        var tagString = $"{tagGroup}:{tagContent}";
        return _tagTypes.GetTag(tagString);
    }

    /// <summary>
    ///     Clears the cached state, forcing recomputation on next access
    /// </summary>
    public async Task ClearCacheAsync()
    {
        await _statePersistent.ClearStateAsync();
    }
}
