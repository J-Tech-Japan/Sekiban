using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Validation;
using System.Diagnostics;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     General implementation of ISekibanExecutor
///     Orchestrates command execution including context creation, handler invocation,
///     tag reservation, and event/tag persistence.
///     Also provides tag state retrieval capabilities.
///     Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralSekibanExecutor : ISekibanExecutor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventPublisher? _eventPublisher;
    private readonly IEventStore _eventStore;

    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _eventPublisher = eventPublisher;
    }

    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 0: Validate command using DataAnnotations attributes
            var commandValidationErrors = CommandValidator.ValidateCommand(command);
            if (commandValidationErrors.Count > 0)
            {
                return ResultBox.Error<ExecutionResult>(new CommandValidationException(commandValidationErrors));
            }

            // Step 1: Create command context
            var commandContext = new GeneralCommandContext(_actorAccessor, _domainTypes);

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
            } else if (eventOrNone.HasEvent)
            {
                collectedEvents.Add(eventOrNone.GetValue());
            }

            // If still no events, return early
            if (collectedEvents.Count == 0)
            {
                return ResultBox.FromValue(
                    new ExecutionResult(Guid.Empty, 0, new List<TagWriteResult>(), stopwatch.Elapsed));
            }

            // Step 3: Collect tags across all events
            var allTags = new HashSet<ITag>(collectedEvents.SelectMany(e => e.Tags));

            // Step 3.1: Validate all tags
            TagValidator.ValidateTagsAndThrow(allTags);

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

                var lastSortableUniqueId = "";

                if (tag is ConsistencyTag ctWithVersion && ctWithVersion.SortableUniqueId.HasValue)
                {
                    lastSortableUniqueId = ctWithVersion.SortableUniqueId.GetValue().Value;
                } else
                {
                    var lookupTag = tag is ConsistencyTag ct ? ct.InnerTag : tag;
                    if (accessedStates.TryGetValue(lookupTag, out var state))
                    {
                        lastSortableUniqueId = state.LastSortedUniqueId;
                    }
                }

                var task = RequestReservationAsync(tag, lastSortableUniqueId, cancellationToken)
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
                } else
                {
                    failedReservations.Add((tag, result.GetException()));
                }
            }

            // If any reservations failed, cancel all successful reservations
            if (failedReservations.Any())
            {
                await CancelReservationsAsync(reservations, cancellationToken);

                var errorMessage = string.Join(
                    "; ",
                    failedReservations.Select(f => $"Tag {f.Tag.GetTag()}: {f.Error.Message}"));
                return ResultBox.Error<ExecutionResult>(
                    new InvalidOperationException($"Failed to reserve tags: {errorMessage}"));
            }

            try
            {
                // Step 5: Write event to EventStore (handles both events and tags)
                var eventId = Guid.NewGuid();
                var sortableId = SortableUniqueId.GenerateNew();
                var metadata = new EventMetadata(eventId.ToString(), command.GetType().Name, "GeneralSekibanExecutor");

                // Build Event objects for each collected event payload
                var events = new List<Event>();
                foreach (var e in collectedEvents)
                {
                    var eId = Guid.NewGuid();
                    var sortable = SortableUniqueId.GenerateNew();
                    var meta = new EventMetadata(eId.ToString(), command.GetType().Name, "GeneralSekibanExecutor");
                    events.Add(
                        new Event(
                            e.Event,
                            sortable,
                            e.Event.GetType().Name,
                            eId,
                            meta,
                            e.Tags.Select(t => t.GetTag()).ToList()));
                }

                var writeResult = await _eventStore.WriteEventsAsync(events);
                if (!writeResult.IsSuccess)
                {
                    await CancelReservationsAsync(reservations, cancellationToken);
                    return ResultBox.Error<ExecutionResult>(writeResult.GetException());
                }

                var (writtenEvents, tagWriteResults) = writeResult.GetValue();

                // Step 6: Confirm reservations with TagConsistentActors
                await ConfirmReservationsAsync(reservations, cancellationToken);

                var firstEvent = writtenEvents.First();

                if (_eventPublisher != null)
                {
                    var publishEvents = writtenEvents
                        .Select((we, idx) => (Event: we,
                            Tags: (IReadOnlyCollection<ITag>)collectedEvents[idx].Tags.AsReadOnly()))
                        .ToList()
                        .AsReadOnly();
                    _ = Task.Run(() => _eventPublisher.PublishAsync(publishEvents, CancellationToken.None));
                }

                // Return success result
                return ResultBox.FromValue(
                    new ExecutionResult(
                        firstEvent.Id,
                        writtenEvents.Count, // event count as a placeholder for position (multi-event)
                        tagWriteResults.ToList(),
                        stopwatch.Elapsed,
                        new Dictionary<string, object>
                        {
                            ["EventCount"] = writtenEvents.Count,
                            ["TagCount"] = allTags.Count
                        }));
            }
            catch (Exception)
            {
                // If anything fails after reservations, cancel them
                await CancelReservationsAsync(reservations, cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ExecutionResult>(ex);
        }
    }

    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        ICommandHandler<TCommand> handler,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        // Delegate to the function-based implementation
        await ExecuteAsync(command, handler.HandleAsync, cancellationToken);

    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>
    {
        // Delegate to the function-based implementation using the command's own handler
        return await ExecuteAsync(command, (cmd, context) => cmd.HandleAsync(context), cancellationToken);
    }

    public async Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId)
    {
        try
        {
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
            if (state.TagPayloadName == nameof(EmptyTagStatePayload))
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
            var payloadTypeResult = _domainTypes.TagStatePayloadTypes.GetPayloadType(state.TagPayloadName);
            if (payloadTypeResult == null)
            {
                return ResultBox.Error<TagState>(
                    new InvalidOperationException($"Unknown payload type: {state.TagPayloadName}"));
            }

            var deserializeResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
                state.TagPayloadName,
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

    private async Task<ResultBox<TagWriteReservation>> RequestReservationAsync(
        ITag tag,
        string lastSortableUniqueId,
        CancellationToken cancellationToken)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (!actorResult.IsSuccess)
            {
                return ResultBox.Error<TagWriteReservation>(actorResult.GetException());
            }

            var actor = actorResult.GetValue();
            return await actor.MakeReservationAsync(lastSortableUniqueId);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagWriteReservation>(ex);
        }
    }

    private async Task CancelReservationsAsync(
        Dictionary<ITag, TagWriteReservation> reservations,
        CancellationToken cancellationToken)
    {
        var cancelTasks = new List<Task>();

        foreach (var (tag, reservation) in reservations)
        {
            var task = CancelReservationAsync(tag, reservation, cancellationToken);
            cancelTasks.Add(task);
        }

        await Task.WhenAll(cancelTasks);
    }

    private async Task CancelReservationAsync(
        ITag tag,
        TagWriteReservation reservation,
        CancellationToken cancellationToken)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (actorResult.IsSuccess)
            {
                var actor = actorResult.GetValue();
                await actor.CancelReservationAsync(reservation);
            }
        }
        catch
        {
            // Best effort - log error in production
        }
    }

    private async Task ConfirmReservationsAsync(
        Dictionary<ITag, TagWriteReservation> reservations,
        CancellationToken cancellationToken)
    {
        var confirmTasks = new List<Task>();

        foreach (var (tag, reservation) in reservations)
        {
            var task = ConfirmReservationAsync(tag, reservation, cancellationToken);
            confirmTasks.Add(task);
        }

        await Task.WhenAll(confirmTasks);
    }

    private async Task ConfirmReservationAsync(
        ITag tag,
        TagWriteReservation reservation,
        CancellationToken cancellationToken)
    {
        try
        {
            var tagConsistentActorId = tag.GetTag();
            var actorResult = await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);

            if (actorResult.IsSuccess)
            {
                var actor = actorResult.GetValue();
                await actor.ConfirmReservationAsync(reservation);
            }
        }
        catch
        {
            // Best effort - log error in production
        }
    }
}
