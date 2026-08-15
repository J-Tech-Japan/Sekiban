using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Validation;
using System.Diagnostics;
using CoreCommandContext = Sekiban.Dcb.Commands.CoreGeneralCommandContext;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Core implementation of command executor logic using ResultBox for error handling.
///     Orchestrates command execution including context creation, handler invocation,
///     tag reservation, and event/tag persistence.
///     Also provides tag state retrieval and query execution capabilities.
///     This is the shared implementation used by both WithResult and WithoutResult packages.
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class CoreGeneralSekibanExecutor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventPublisher? _eventPublisher;
    private readonly IEventStore _eventStore;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ISortableUniqueIdGenerator _sortableUniqueIdGenerator;
    private readonly SortableUniqueIdSeedCoordinator _sortableUniqueIdSeedCoordinator;
    private readonly SortableUniqueIdWaitPolicy _sortableUniqueIdWaitPolicy;

    /// <summary>
    ///     Test seam ONLY (never set in production): the EventId / SortableUniqueId generators used by the serialized
    ///     conditional-commit path. Making the allocation stage injectable lets a test prove it is NOT reached when an
    ///     unsupported wire version is rejected first — a direct, non-vacuous ordering probe. Defaults are the real
    ///     generators, so production behaviour is unchanged.
    /// </summary>
    internal Func<Guid> ConditionalEventIdFactory { get; set; } = Guid.CreateVersion7;
    internal Func<string>? ConditionalSortableIdFactory { get; set; }

    private const string DefaultExecutedUser = "GeneralSekibanExecutor";
    private const string SerializedExecutedUser = "SerializedSekibanExecutor";
    private readonly IExecutedUserProvider? _executedUserProvider;

    /// <summary>
    ///     Binary-compatible overload preserved for callers compiled against the pre-SEK-G23 constructor.
    /// </summary>
    public CoreGeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher)
        : this(eventStore, actorAccessor, domainTypes, eventPublisher, null)
    {
    }

    public CoreGeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null,
        IExecutedUserProvider? executedUserProvider = null)
        : this(
            eventStore,
            actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            ProcessSharedSortableUniqueIdServices.Generator,
            ProcessSharedSortableUniqueIdServices.SeedCoordinator,
            new DefaultServiceIdProvider(),
            SortableUniqueIdWaitPolicy.System)
    {
    }

    /// <summary>Creates an executor with the process-wide allocator and retryable service-head seed coordinator.</summary>
    public CoreGeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider)
        : this(
            eventStore,
            actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            serviceIdProvider,
            SortableUniqueIdWaitPolicy.System)
    {
    }

    internal CoreGeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventPublisher = eventPublisher;
        _executedUserProvider = executedUserProvider;
        _sortableUniqueIdGenerator = sortableUniqueIdGenerator ??
                                     throw new ArgumentNullException(nameof(sortableUniqueIdGenerator));
        _sortableUniqueIdSeedCoordinator = sortableUniqueIdSeedCoordinator ??
                                           throw new ArgumentNullException(nameof(sortableUniqueIdSeedCoordinator));
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _sortableUniqueIdWaitPolicy = sortableUniqueIdWaitPolicy ??
                                      throw new ArgumentNullException(nameof(sortableUniqueIdWaitPolicy));
    }

    private Task EnsureSortableUniqueIdSeededAsync(CancellationToken cancellationToken)
    {
        var serviceId = ServiceIdValidator.NormalizeAndValidate(_serviceIdProvider.GetCurrentServiceId());
        return _sortableUniqueIdSeedCoordinator.EnsureSeededAsync(serviceId, _eventStore, cancellationToken);
    }

    private string GetExecutedUser()
    {
        var value = _executedUserProvider?.GetExecutedUser();
        return string.IsNullOrEmpty(value) ? DefaultExecutedUser : value;
    }

    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 0: Validate command using DataAnnotations attributes
            var validationErrors = SekibanValidator.Validate(command);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<ExecutionResult>(new SekibanValidationException(validationErrors));
            }

            // Step 1: Create command context
            var commandContext = new CoreCommandContext(_actorAccessor, _domainTypes);

            // Step 2: Execute handler function with context
            var handlerResult = await handlerFunc(command, commandContext);
            if (!handlerResult.IsSuccess)
            {
                return ResultBox.Error<ExecutionResult>(handlerResult.GetException());
            }

            var eventOrNone = handlerResult.GetValue();

            // Collect events appended explicitly via context (multi-event support)
            var appended = commandContext.GetAppendedEvents();

            // If handler returned an event (EventOrNone.HasEvent), include it only if it is not already in appended list
            // Current AppendEvent returns EventOrNone.Event for the appended event; some handlers may both AppendEvent and return the last event.
            // We treat appended list as the source of truth for multiple events; if none appended but return has event, use that single one.
            var collectedEvents = new List<EventPayloadWithTags>();
            if (appended.Count > 0)
            {
                collectedEvents.AddRange(appended);
                // If handler also returned an event that is different reference (just in case), append if not duplicate
                if (eventOrNone.HasEvent)
                {
                    var returned = eventOrNone.GetValue();
                    if (!collectedEvents.Contains(returned))
                    {
                        collectedEvents.Add(returned);
                    }
                }
            }
            else if (eventOrNone.HasEvent)
            {
                collectedEvents.Add(eventOrNone.GetValue());
            }

            // If still no events, return early
            if (collectedEvents.Count == 0)
            {
                return ResultBox.FromValue(
                    new ExecutionResult(Guid.Empty, 0, new List<TagWriteResult>(), stopwatch.Elapsed, []));
            }

            // Step 3: Collect tags across all events
            var allTags = new HashSet<ITag>(collectedEvents.SelectMany(e => e.Tags));

            // Step 3.1: Validate all tags
            TagValidator.ValidateTagsAndThrow(allTags);

            // Establish the persisted floor before any reservation, id allocation, or write.
            await EnsureSortableUniqueIdSeededAsync(cancellationToken);

            // Step 4: According to spec:
            //  - If tag.IsConsistencyTag() == false -> DO NOT reserve (skip)
            //  - If tag.IsConsistencyTag() == true AND tag is ConsistencyTag with SortableUniqueId present -> use that SortableUniqueId
            //  - If tag.IsConsistencyTag() == true AND (ConsistencyTag without SortableUniqueId OR not ConsistencyTag class) ->
            //       look up accessed tag state via ICommandContext (GeneralCommandContext) and use its LastSortableUniqueId
            var reservations = new Dictionary<ITag, TagWriteReservation>();
            var reservationTasks = new List<Task<(ITag Tag, ResultBox<TagWriteReservation> Result)>>();
            var accessedStates = commandContext.GetAccessedTagStates();

            foreach (var tag in allTags)
            {
                if (!tag.IsConsistencyTag())
                {
                    continue; // skip non-consistency tags (no reservation)
                }

                string? lastSortableUniqueId = null;

                if (tag is ConsistencyTag ctWithVersion && ctWithVersion.SortableUniqueId.HasValue)
                {
                    lastSortableUniqueId = ctWithVersion.SortableUniqueId.GetValue().Value;
                }
                else
                {
                    var lookupTag = tag is ConsistencyTag ct ? ct.InnerTag : tag;
                    if (accessedStates.TryGetValue(lookupTag, out var state))
                    {
                        lastSortableUniqueId = state.LastSortedUniqueId;
                    }
                }

                var task = TagReservationHelper.RequestReservationAsync(_actorAccessor, tag, lastSortableUniqueId)
                    .ContinueWith(t => (tag, t.Result), cancellationToken);
                reservationTasks.Add(task);
            }

            var reservationResults = await Task.WhenAll(reservationTasks);

            // Check if all reservations succeeded
            var failedReservations = new List<(ITag Tag, Exception Error)>();
            foreach (var (tag, result) in reservationResults)
            {
                if (result.IsSuccess)
                {
                    reservations[tag] = result.GetValue();
                }
                else
                {
                    failedReservations.Add((tag, result.GetException()));
                }
            }

            // If any reservations failed, cancel all successful reservations
            if (failedReservations.Any())
            {
                await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);

                var errorMessage = string.Join(
                    "; ",
                    failedReservations.Select(f => $"Tag {f.Tag.GetTag()}: {f.Error.Message}"));
                return ResultBox.Error<ExecutionResult>(
                    new InvalidOperationException($"Failed to reserve tags: {errorMessage}"));
            }

            try
            {
                // Step 5: Write event to EventStore (handles both events and tags)
                // Build Event objects for each collected event payload
                var executedUser = GetExecutedUser();
                var events = new List<Event>();
                foreach (var e in collectedEvents)
                {
                    var eId = Guid.CreateVersion7();
                    var sortable = _sortableUniqueIdGenerator.GenerateNew();
                    var meta = new EventMetadata(eId.ToString(), command.GetType().Name, executedUser);
                    events.Add(
                        new Event(
                            e.Event,
                            sortable,
                            e.Event.GetType().Name,
                            eId,
                            meta,
                            e.Tags.Select(t => t.GetTag()).ToList()));
                }

                var writeResult = await _eventStore.WriteEventsAsync(events, _domainTypes.EventTypes);
                if (!writeResult.IsSuccess)
                {
                    await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);
                    return ResultBox.Error<ExecutionResult>(writeResult.GetException());
                }

                var (writtenEvents, tagWriteResults) = writeResult.GetValue();

                // Step 6: Confirm reservations with TagConsistentActors
                await TagReservationHelper.ConfirmReservationsAsync(_actorAccessor, reservations);

                // Step 6.1: Notify non-consistency tags about the event write
                await TagReservationHelper.NotifyNonConsistencyTagsAsync(_actorAccessor, allTags, reservations.Keys);

                var firstEvent = writtenEvents.First();

                if (_eventPublisher != null)
                {
                    var publishEvents = writtenEvents
                        .Select((we, idx) => (Event: we,
                            Tags: (IReadOnlyCollection<ITag>)collectedEvents[idx].Tags.AsReadOnly()))
                        .ToList()
                        .AsReadOnly();
                    await _eventPublisher.PublishAsync(publishEvents, CancellationToken.None);
                }

                // Return success result
                return ResultBox.FromValue(
                    new ExecutionResult(
                        firstEvent.Id,
                        writtenEvents.Count, // event count as a placeholder for position (multi-event)
                        tagWriteResults.ToList(),
                        stopwatch.Elapsed,
                        writtenEvents,
                        new Dictionary<string, object>
                        {
                            ["EventCount"] = writtenEvents.Count,
                            ["TagCount"] = allTags.Count
                        },
                        firstEvent.SortableUniqueIdValue));
            }
            catch (Exception)
            {
                // If anything fails after reservations, cancel them
                await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);
                throw;
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ExecutionResult>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId)
    {
        try
        {
            // Step 0: Validate tagStateId using DataAnnotations attributes
            var validationErrors = SekibanValidator.Validate(tagStateId);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<TagState>(new SekibanValidationException(validationErrors));
            }

            // Get the tag state actor for this tag state ID
            var tagStateActorId = tagStateId.GetTagStateId();
            var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);

            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<TagState>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();

            // Get the state from the actor
            var state = await actor.GetStateAsync();

            // Convert SerializableTagState to TagState
            // We need to deserialize the payload from the serializable state
            if (state.ResolvedPayloadName == nameof(EmptyTagStatePayload))
            {
                return ResultBox.FromValue(
                    new TagState(
                        new EmptyTagStatePayload(),
                        state.Version,
                        state.LastSortedUniqueId,
                        state.TagGroup,
                        state.TagContent,
                        state.TagProjector,
                        state.ProjectorVersion));
            }

            // Deserialize the payload using domain types
            var payloadTypeResult = _domainTypes.TagStatePayloadTypes.GetPayloadType(state.ResolvedPayloadName);
            if (payloadTypeResult == null)
            {
                return ResultBox.Error<TagState>(
                    new InvalidOperationException($"Unknown payload type: {state.ResolvedPayloadName}"));
            }

            var deserializeResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
                state.ResolvedPayloadName,
                state.Payload);

            if (!deserializeResult.IsSuccess)
            {
                return ResultBox.Error<TagState>(deserializeResult.GetException());
            }

            var payload = deserializeResult.GetValue();

            return ResultBox.FromValue(
                new TagState(
                    payload,
                    state.Version,
                    state.LastSortedUniqueId,
                    state.TagGroup,
                    state.TagContent,
                    state.TagProjector,
                    state.ProjectorVersion));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)
    {
        try
        {
            var validationErrors = SekibanValidator.Validate(tagStateId);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<SerializableTagState>(new SekibanValidationException(validationErrors));
            }

            var tagStateActorId = tagStateId.GetTagStateId();
            var actorResult = await _actorAccessor.GetActorAsync<ITagStateActorCommon>(tagStateActorId);

            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<SerializableTagState>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();
            var state = await actor.GetStateAsync();

            return ResultBox.FromValue(state);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializableTagState>(ex);
        }
    }

    public async Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (request.ConsistencyTags.Any(entry => entry.LastSortableUniqueId is null))
            {
                return ResultBox.Error<SerializedCommitResult>(
                    new ArgumentException(
                        "Serialized consistency-tag reservations require a non-null lastSortableUniqueId. " +
                        "Use an empty string to assert that the tag is empty."));
            }

            if (request.EventCandidates.Count == 0)
            {
                return ResultBox.FromValue(
                    new SerializedCommitResult(
                        Array.Empty<SerializableEvent>(),
                        Array.Empty<TagWriteResult>(),
                        stopwatch.Elapsed));
            }

            // Step 1: Collect all tags from event candidates
            var allTagStrings = new HashSet<string>(
                request.EventCandidates.SelectMany(e => e.Tags));

            var duplicateConsistencyTags = request.ConsistencyTags
                .GroupBy(ct => ct.Tag)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicateConsistencyTags.Count > 0)
            {
                return ResultBox.Error<SerializedCommitResult>(
                    new InvalidOperationException(
                        $"Duplicate consistency tags in request: {string.Join(", ", duplicateConsistencyTags)}"));
            }

            var unknownConsistencyTags = request.ConsistencyTags
                .Select(ct => ct.Tag)
                .Where(tag => !allTagStrings.Contains(tag))
                .Distinct()
                .ToList();
            if (unknownConsistencyTags.Count > 0)
            {
                return ResultBox.Error<SerializedCommitResult>(
                    new InvalidOperationException(
                        $"Consistency tags must exist in event candidate tags. Unknown tags: {string.Join(", ", unknownConsistencyTags)}"));
            }

            // Step 2: Build FallbackTag objects for non-consistency tags and reservation
            var consistencyEntryMap = request.ConsistencyTags.ToDictionary(
                ct => ct.Tag,
                ct => ct.LastSortableUniqueId);

            var allTags = new HashSet<ITag>();
            foreach (var tagString in allTagStrings)
            {
                allTags.Add(ParseTag(tagString, consistencyEntryMap.ContainsKey(tagString)));
            }

            TagValidator.ValidateTagsAndThrow(allTags);

            // Establish the persisted floor before any reservation, id allocation, or write.
            await EnsureSortableUniqueIdSeededAsync(cancellationToken);

            // Step 3: Reserve consistency tags
            var reservations = new Dictionary<ITag, TagWriteReservation>();
            var failedReservations = new List<(ITag Tag, Exception Error)>();
            foreach (var tag in allTags)
            {
                if (!tag.IsConsistencyTag())
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var lastSortableUniqueId = consistencyEntryMap[tag.GetTag()];
                var result = await TagReservationHelper.RequestReservationAsync(_actorAccessor, tag, lastSortableUniqueId);
                if (result.IsSuccess)
                {
                    reservations[tag] = result.GetValue();
                }
                else
                {
                    failedReservations.Add((tag, result.GetException()));
                }
            }

            if (failedReservations.Any())
            {
                await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);

                var errorMessage = string.Join(
                    "; ",
                    failedReservations.Select(f => $"Tag {f.Tag.GetTag()}: {f.Error.Message}"));
                return ResultBox.Error<SerializedCommitResult>(
                    new InvalidOperationException($"Failed to reserve tags: {errorMessage}"));
            }

            var reservationsConfirmed = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Step 4: Build SerializableEvent objects with server-generated metadata
                var serializableEvents = new List<SerializableEvent>();
                foreach (var candidate in request.EventCandidates)
                {
                    var eventId = ConditionalEventIdFactory();
                    var sortableId = (ConditionalSortableIdFactory ?? _sortableUniqueIdGenerator.GenerateNew)();
                    var metadata = new EventMetadata(eventId.ToString(), "SerializedCommit", "SerializedSekibanExecutor");

                    serializableEvents.Add(new SerializableEvent(
                        candidate.Payload,
                        sortableId,
                        eventId,
                        metadata,
                        candidate.Tags.ToList(),
                        candidate.EventPayloadName));
                }

                // Step 5: Write serializable events
                var writeResult = await _eventStore.WriteSerializableEventsAsync(serializableEvents);
                if (!writeResult.IsSuccess)
                {
                    await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);
                    return ResultBox.Error<SerializedCommitResult>(writeResult.GetException());
                }

                var (writtenEvents, tagWriteResults) = writeResult.GetValue();

                // Step 6: Confirm reservations
                await TagReservationHelper.ConfirmReservationsAsync(_actorAccessor, reservations);
                reservationsConfirmed = true;

                cancellationToken.ThrowIfCancellationRequested();

                // Step 6.1: Notify non-consistency tags
                await TagReservationHelper.NotifyNonConsistencyTagsAsync(_actorAccessor, allTags, reservations.Keys);

                if (_eventPublisher != null && writtenEvents.Count > 0)
                {
                    var publishEvents = new List<(Event Event, IReadOnlyCollection<ITag> Tags)>(writtenEvents.Count);
                    foreach (var writtenEvent in writtenEvents)
                    {
                        var eventResult = writtenEvent.ToEvent(_domainTypes.EventTypes);
                        if (!eventResult.IsSuccess)
                        {
                            return ResultBox.Error<SerializedCommitResult>(eventResult.GetException());
                        }

                        List<ITag> eventTags = writtenEvent.Tags
                            .Select(_domainTypes.TagTypes.GetTag)
                            .ToList();
                        publishEvents.Add((eventResult.GetValue(), eventTags.AsReadOnly()));
                    }

                    await _eventPublisher.PublishAsync(publishEvents.AsReadOnly(), CancellationToken.None);
                }

                return ResultBox.FromValue(
                    new SerializedCommitResult(
                        writtenEvents,
                        tagWriteResults,
                        stopwatch.Elapsed));
            }
            catch (Exception)
            {
                if (!reservationsConfirmed && reservations.Count > 0)
                {
                    await TagReservationHelper.CancelReservationsAsync(_actorAccessor, reservations);
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializedCommitResult>(ex);
        }
    }

    public async Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
    {
        try
        {
            // Step 0: Validate query using DataAnnotations attributes
            var validationErrors = SekibanValidator.Validate(queryCommon);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<TResult>(new SekibanValidationException(validationErrors));
            }

            // Get the multi-projector type for this query
            var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
            if (!projectorTypeResult.IsSuccess)
            {
                return ResultBox.Error<TResult>(projectorTypeResult.GetException());
            }

            var projectorType = projectorTypeResult.GetValue();

            // Get the multi-projector name
            var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
            if (projectorNameProperty == null)
            {
                return ResultBox.Error<TResult>(
                    new InvalidOperationException(
                        $"Projector type {projectorType.Name} does not have MultiProjectorName property"));
            }

            var projectorName = projectorNameProperty.GetValue(null) as string;
            if (string.IsNullOrEmpty(projectorName))
            {
                return ResultBox.Error<TResult>(
                    new InvalidOperationException(
                        $"Projector type {projectorType.Name} has invalid MultiProjectorName"));
            }

            // Get the multi-projection actor
            var actorResult = await _actorAccessor.GetActorAsync<GeneralMultiProjectionActor>(projectorName);
            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<TResult>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();

            await WaitForStrictSortableUniqueIdIfNeededAsync(
                actor,
                queryCommon,
                SortableUniqueIdWaitSurface.InMemorySingle);

            // Get the current state
            var stateResult = await actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<TResult>(stateResult.GetException());
            }

            var state = stateResult.GetValue();

            // Create a provider function that returns the payload
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload));

            // Execute the query using QueryTypes
            var serviceProvider = _actorAccessor as IServiceProvider ??
                throw new InvalidOperationException("ActorAccessor must implement IServiceProvider");

            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;
            try
            {
                var accessorType = state.Payload.GetType();
                // If the payload implements ISafeAndUnsafeStateAccessor<T>, it has SafeVersion property.
                var safeVersionProp = accessorType.GetProperty("SafeVersion");
                if (safeVersionProp != null)
                {
                    safeVersion = safeVersionProp.GetValue(state.Payload) as int?;
                }
                var unsafeVersionProp = accessorType.GetProperty("GetVersion") ?? accessorType.GetProperty("Version");
                // fallback: use state.Version as unsafe version
                unsafeVersion = state.Version;
                // Attempt to get current safe window threshold from actor (more precise than last safe id)
                var actorSafeThreshold = actor.PeekCurrentSafeWindowThreshold();
                safeThreshold = actorSafeThreshold.Value;
                try { safeThresholdTime = actorSafeThreshold.GetDateTime(); } catch { }
            }
            catch { }
            var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(
                queryCommon,
                projectorProvider,
                serviceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            if (!result.IsSuccess)
            {
                return ResultBox.Error<TResult>(result.GetException());
            }

            var value = result.GetValue();
            if (value is TResult typedResult)
            {
                return ResultBox.FromValue(typedResult);
            }

            return ResultBox.Error<TResult>(
                new InvalidCastException(
                    $"Query result type mismatch. Expected {typeof(TResult).Name}, got {value?.GetType().Name ?? "null"}"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TResult>(ex);
        }
    }

    public async Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        try
        {
            // Step 0: Validate list query using DataAnnotations attributes
            var validationErrors = SekibanValidator.Validate(queryCommon);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(new SekibanValidationException(validationErrors));
            }

            // Get the multi-projector type for this query
            var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
            if (!projectorTypeResult.IsSuccess)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(projectorTypeResult.GetException());
            }

            var projectorType = projectorTypeResult.GetValue();

            // Get the multi-projector name
            var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
            if (projectorNameProperty == null)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(
                    new InvalidOperationException(
                        $"Projector type {projectorType.Name} does not have MultiProjectorName property"));
            }

            var projectorName = projectorNameProperty.GetValue(null) as string;
            if (string.IsNullOrEmpty(projectorName))
            {
                return ResultBox.Error<ListQueryResult<TResult>>(
                    new InvalidOperationException(
                        $"Projector type {projectorType.Name} has invalid MultiProjectorName"));
            }

            // Get the multi-projection actor
            var actorResult = await _actorAccessor.GetActorAsync<GeneralMultiProjectionActor>(projectorName);
            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();

            await WaitForStrictSortableUniqueIdIfNeededAsync(
                actor,
                queryCommon,
                SortableUniqueIdWaitSurface.InMemoryList);

            // Get the current state
            var stateResult = await actor.GetStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(stateResult.GetException());
            }

            var state = stateResult.GetValue();

            // Create a provider function that returns the payload
            var projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload));

            // Execute the list query using QueryTypes
            var serviceProvider = _actorAccessor as IServiceProvider ??
                throw new InvalidOperationException("ActorAccessor must implement IServiceProvider");

            int? safeVersion = null;
            string? safeThreshold = null;
            DateTime? safeThresholdTime = null;
            int? unsafeVersion = null;
            try
            {
                var accessorType = state.Payload.GetType();
                var safeVersionProp = accessorType.GetProperty("SafeVersion");
                if (safeVersionProp != null)
                {
                    safeVersion = safeVersionProp.GetValue(state.Payload) as int?;
                }
                unsafeVersion = state.Version;
                var actorSafeThreshold = actor.PeekCurrentSafeWindowThreshold();
                safeThreshold = actorSafeThreshold.Value;
                try { safeThresholdTime = actorSafeThreshold.GetDateTime(); } catch { }
            }
            catch { }
            var result = await _domainTypes.QueryTypes.ExecuteListQueryAsync(
                queryCommon,
                projectorProvider,
                serviceProvider,
                safeVersion,
                safeThreshold,
                safeThresholdTime,
                unsafeVersion);

            if (!result.IsSuccess)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(result.GetException());
            }

            var value = result.GetValue();
            if (value is ListQueryResult<TResult> typedResult)
            {
                return ResultBox.FromValue(typedResult);
            }

            return ResultBox.Error<ListQueryResult<TResult>>(
                new InvalidCastException(
                    $"Query result type mismatch. Expected ListQueryResult<{typeof(TResult).Name}>, got {value?.GetType().Name ?? "null"}"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ListQueryResult<TResult>>(ex);
        }
    }

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
        _eventStore.GetLatestSortableUniqueIdAsync();

    public async Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null)
    {
        try
        {
            var projectorVersionResult = ProjectionHeadStatusUtilities.ValidateProjectorVersion(
                _domainTypes,
                projectorName,
                expectedProjectorVersion);
            if (!projectorVersionResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionHeadStatus>(projectorVersionResult.GetException());
            }

            var actorResult = await _actorAccessor.GetActorAsync<GeneralMultiProjectionActor>(projectorName);
            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionHeadStatus>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();
            var status = await actor.GetProjectionHeadStatusAsync();

            var projectorNameResult = ProjectionHeadStatusUtilities.EnsureProjectorNameConsistency(
                projectorName,
                status.ProjectorName);
            if (!projectorNameResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionHeadStatus>(projectorNameResult.GetException());
            }

            var projectorVersionConsistencyResult = ProjectionHeadStatusUtilities.EnsureProjectorVersionConsistency(
                projectorVersionResult.GetValue(),
                status.ProjectorVersion);
            if (!projectorVersionConsistencyResult.IsSuccess)
            {
                return ResultBox.Error<ProjectionHeadStatus>(projectorVersionConsistencyResult.GetException());
            }

            return ResultBox.FromValue(status with
            {
                ProjectorName = projectorNameResult.GetValue(),
                ProjectorVersion = projectorVersionConsistencyResult.GetValue()
            });
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionHeadStatus>(ex);
        }
    }

    private async Task WaitForStrictSortableUniqueIdIfNeededAsync(
        GeneralMultiProjectionActor actor,
        object query,
        SortableUniqueIdWaitSurface surface)
    {
        if (query is not IStrictWaitForSortableUniqueId strictQuery ||
            string.IsNullOrEmpty(strictQuery.WaitForSortableUniqueId))
        {
            // Legacy InMemory queries intentionally keep their historical no-wait behavior.
            return;
        }

        var target = strictQuery.WaitForSortableUniqueId;
        var wait = await _sortableUniqueIdWaitPolicy.WaitAsync(
            target,
            surface,
            SortableUniqueIdWaitMode.Strict,
            _ => actor.IsSortableUniqueIdReceived(target),
            _ => ReadCurrentSortableUniqueIdAsync(actor));

        if (wait.TimedOut)
        {
            throw new SortableUniqueIdWaitTimeoutException(
                target,
                wait.Timeout,
                wait.Elapsed,
                wait.LastObservedSortableUniqueId);
        }
    }

    private static async Task<string?> ReadCurrentSortableUniqueIdAsync(GeneralMultiProjectionActor actor)
    {
        var status = await actor.GetProjectionHeadStatusAsync().ConfigureAwait(false);
        return status.Current.LastSortableUniqueId;
    }

    public async Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false)
    {
        try
        {
            var latestSortableUniqueIdResult = await _eventStore.GetLatestSortableUniqueIdAsync();
            if (!latestSortableUniqueIdResult.IsSuccess)
            {
                return ResultBox.Error<EventStoreHeadStatus>(latestSortableUniqueIdResult.GetException());
            }

            long? totalEventCount = null;
            if (includeTotalEventCount)
            {
                var totalEventCountResult = await _eventStore.GetEventCountAsync();
                if (!totalEventCountResult.IsSuccess)
                {
                    return ResultBox.Error<EventStoreHeadStatus>(totalEventCountResult.GetException());
                }

                totalEventCount = totalEventCountResult.GetValue();
            }

            return ResultBox.FromValue(new EventStoreHeadStatus(
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(latestSortableUniqueIdResult.GetValue()),
                totalEventCount));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<EventStoreHeadStatus>(ex);
        }
    }

    /// <summary>
    ///     Parses a tag string into an ITag for serialized commit.
    ///     Consistency tags are wrapped in ConsistencyTag, others in FallbackTag.
    /// </summary>
    private static ITag ParseTag(string tagString, bool isConsistency)
    {
        var parts = tagString.Split(':', 2);
        var group = parts[0];
        var content = parts.Length > 1 ? parts[1] : "";
        var innerTag = new FallbackTag(group, content);

        if (isConsistency)
        {
            return new ConsistencyTag(innerTag);
        }

        return innerTag;
    }


    // ---- SEK-G15 conditional (unique-key) append: opt-in and additive. Everything above is unchanged; the conditional
    // path lives entirely in the methods below so the unconditional pipeline keeps its exact behaviour. Types from the
    // Capabilities namespace are fully qualified so no using directive changes above are required. ----

    /// <summary>
    ///     Opt-in overload carrying execution options. With no conditional option it delegates to the unconditional
    ///     pipeline unchanged; with a conditional-append option it runs the dedicated single-event conditional pipeline,
    ///     which fails closed BEFORE the handler on a store that cannot enforce the condition.
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CommandExecutionOptions? options,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        options?.ConditionalAppend is { } conditional
            ? ExecuteConditionalAppendAsync(command, handlerFunc, conditional, cancellationToken)
            : ExecuteAsync(command, handlerFunc, cancellationToken);

    private async Task<ResultBox<ExecutionResult>> ExecuteConditionalAppendAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        ConditionalAppendSpecification conditional,
        CancellationToken cancellationToken) where TCommand : ICommand
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Step 0: validate command.
            var validationErrors = SekibanValidator.Validate(command);
            if (validationErrors.Count > 0)
            {
                return ResultBox.Error<ExecutionResult>(new SekibanValidationException(validationErrors));
            }

            // Step 0.5: FAIL CLOSED before the handler runs — before any EventId allocation, serialization, or store
            // call. The decision is the runtime capability descriptor (never a type name); the cast is defensive (a store
            // that declares support must implement the interface — enforced by an architecture test).
            var capability = Sekiban.Dcb.Capabilities.SekibanDcbCapabilityResolver.DescribeWriteConditions(_eventStore, "event store");
            if (!capability.Supports(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey))
            {
                return ResultBox.Error<ExecutionResult>(
                    new ConditionNotSupportedException(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey, capability.ProviderName));
            }

            if (_eventStore is not IConditionalEventStore conditionalStore)
            {
                return ResultBox.Error<ExecutionResult>(
                    new ConditionNotSupportedException(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey, capability.ProviderName));
            }

            // Step 1-2: context + handler.
            var commandContext = new CoreCommandContext(_actorAccessor, _domainTypes);
            var handlerResult = await handlerFunc(command, commandContext);
            if (!handlerResult.IsSuccess)
            {
                return ResultBox.Error<ExecutionResult>(handlerResult.GetException());
            }

            var eventOrNone = handlerResult.GetValue();
            var collectedEvents = new List<EventPayloadWithTags>(commandContext.GetAppendedEvents());
            if (eventOrNone.HasEvent)
            {
                var returned = eventOrNone.GetValue();
                if (!collectedEvents.Contains(returned))
                {
                    collectedEvents.Add(returned);
                }
            }

            // Single-event contract: zero AND multiple both fail closed here, BEFORE any store call — a zero-event
            // conditional command must never fall through to the legacy empty-success result.
            if (collectedEvents.Count != 1)
            {
                return ResultBox.Error<ExecutionResult>(
                    new SingleEventConditionalAppendException(collectedEvents.Count));
            }

            var single = collectedEvents[0];
            TagValidator.ValidateTagsAndThrow(new HashSet<ITag>(single.Tags));

            await EnsureSortableUniqueIdSeededAsync(cancellationToken);

            var executedUser = GetExecutedUser();
            var eventId = ConditionalEventIdFactory();
            var sortable = _sortableUniqueIdGenerator.GenerateNew();
            var metadata = new EventMetadata(eventId.ToString(), command.GetType().Name, executedUser);
            var domainEvent = new Event(
                single.Event,
                sortable,
                single.Event.GetType().Name,
                eventId,
                metadata,
                single.Tags.Select(t => t.GetTag()).ToList());
            var serializable = domainEvent.ToSerializableEvent(_domainTypes.EventTypes);

            var appendResult = await conditionalStore.AppendIfUniqueAsync(
                new ConditionalAppendRequest(conditional.IdempotencyKey, serializable),
                cancellationToken);
            if (!appendResult.IsSuccess)
            {
                return ResultBox.Error<ExecutionResult>(appendResult.GetException());
            }

            var receipt = appendResult.GetValue();
            var isNewlyAppended = receipt.Status == ConditionalAppendStatus.Appended;
            var writtenEvents = isNewlyAppended ? new List<Event> { domainEvent } : new List<Event>();

            if (_eventPublisher != null && isNewlyAppended)
            {
                await _eventPublisher.PublishAsync(
                    new List<(Event Event, IReadOnlyCollection<ITag> Tags)> { (domainEvent, single.Tags.AsReadOnly()) }
                        .AsReadOnly(),
                    CancellationToken.None);
            }

            return ResultBox.FromValue(
                new ExecutionResult(
                    receipt.WinnerEventId,
                    isNewlyAppended ? 1 : 0,
                    new List<TagWriteResult>(),
                    stopwatch.Elapsed,
                    writtenEvents,
                    new Dictionary<string, object>
                    {
                        ["ConditionalAppendStatus"] = receipt.Status.ToString(),
                        ["OperationFingerprint"] = receipt.OperationFingerprint,
                        ["WasAlreadyCommitted"] = receipt.WasAlreadyCommitted
                    },
                    receipt.WinnerSortableUniqueId));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ExecutionResult>(ex);
        }
    }

    /// <summary>
    ///     WASM-boundary conditional single-event serialized commit. Validates the request <c>Version</c> and the store
    ///     capability FAIL-CLOSED — before any EventId allocation, serialization, or store call.
    /// </summary>
    public async Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Version gate first: an unknown wire version is rejected before anything else happens.
            if (request.Version != SerializedConditionalCommitRequest.CurrentVersion)
            {
                return ResultBox.Error<SerializedConditionalCommitResult>(
                    new UnsupportedSerializedCommitVersionException(
                        request.Version,
                        SerializedConditionalCommitRequest.CurrentVersion));
            }

            var capability = Sekiban.Dcb.Capabilities.SekibanDcbCapabilityResolver.DescribeWriteConditions(_eventStore, "event store");
            if (!capability.Supports(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey))
            {
                return ResultBox.Error<SerializedConditionalCommitResult>(
                    new ConditionNotSupportedException(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey, capability.ProviderName));
            }

            if (_eventStore is not IConditionalEventStore conditionalStore)
            {
                return ResultBox.Error<SerializedConditionalCommitResult>(
                    new ConditionNotSupportedException(Sekiban.Dcb.Capabilities.WriteConditionKind.SingleEventUniqueKey, capability.ProviderName));
            }

            var candidate = request.EventCandidate;
            await EnsureSortableUniqueIdSeededAsync(cancellationToken);
            var eventId = ConditionalEventIdFactory();
            var sortableId = (ConditionalSortableIdFactory ?? _sortableUniqueIdGenerator.GenerateNew)();
            var metadata = new EventMetadata(eventId.ToString(), "SerializedConditionalCommit", "SerializedSekibanExecutor");
            var serializable = new SerializableEvent(
                candidate.Payload,
                sortableId,
                eventId,
                metadata,
                candidate.Tags.ToList(),
                candidate.EventPayloadName);

            var appendResult = await conditionalStore.AppendIfUniqueAsync(
                new ConditionalAppendRequest(request.IdempotencyKey, serializable),
                cancellationToken);
            if (!appendResult.IsSuccess)
            {
                return ResultBox.Error<SerializedConditionalCommitResult>(appendResult.GetException());
            }

            var receipt = appendResult.GetValue();
            var isNewlyAppended = receipt.Status == ConditionalAppendStatus.Appended;
            var written = isNewlyAppended
                ? (IReadOnlyList<SerializableEvent>)new[] { serializable }
                : Array.Empty<SerializableEvent>();

            if (_eventPublisher != null && isNewlyAppended)
            {
                var eventResult = serializable.ToEvent(_domainTypes.EventTypes);
                if (eventResult.IsSuccess)
                {
                    List<ITag> eventTags = serializable.Tags.Select(_domainTypes.TagTypes.GetTag).ToList();
                    await _eventPublisher.PublishAsync(
                        new List<(Event Event, IReadOnlyCollection<ITag> Tags)> { (eventResult.GetValue(), eventTags.AsReadOnly()) }
                            .AsReadOnly(),
                        CancellationToken.None);
                }
            }

            return ResultBox.FromValue(
                new SerializedConditionalCommitResult(
                    SerializedConditionalCommitResult.CurrentVersion,
                    receipt.Status,
                    receipt.WinnerEventId,
                    receipt.WinnerSortableUniqueId,
                    receipt.OperationFingerprint,
                    written,
                    stopwatch.Elapsed));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<SerializedConditionalCommitResult>(ex);
        }
    }
}
