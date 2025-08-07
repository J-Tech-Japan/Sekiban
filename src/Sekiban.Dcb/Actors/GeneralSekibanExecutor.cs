using System.Diagnostics;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.Actors;

/// <summary>
/// General implementation of ISekibanExecutor
/// Orchestrates command execution including context creation, handler invocation,
/// tag reservation, and event/tag persistence.
/// Also provides tag state retrieval capabilities.
/// Can be used with different actor frameworks (InMemory, Orleans, Dapr)
/// </summary>
public class GeneralSekibanExecutor : ISekibanExecutor
{
    private readonly IEventStore _eventStore;
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    
    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }
    
    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Step 1: Create command context
            var commandContext = new GeneralCommandContext(_actorAccessor, _domainTypes);
            
            // Step 2: Execute handler function with context
            var handlerResult = await handlerFunc(command, commandContext);
            if (!handlerResult.IsSuccess)
            {
                return ResultBox.Error<ExecutionResult>(handlerResult.GetException());
            }
            
            var eventOrNone = handlerResult.GetValue();
            
            // If no events to append, return early
            if (!eventOrNone.HasEvent)
            {
                return ResultBox.FromValue(new ExecutionResult(
                    Guid.Empty,
                    0,
                    new List<TagWriteResult>(),
                    stopwatch.Elapsed
                ));
            }
            
            var eventWithTags = eventOrNone.GetValue();
            
            // Step 3: Get all tags that need reservations
            var allTags = new HashSet<ITag>();
            foreach (var tag in eventWithTags.Tags)
            {
                allTags.Add(tag);
            }
            
            // Step 4: Request write reservations from TagConsistentActors
            var reservations = new Dictionary<ITag, TagWriteReservation>(); // tag -> reservation
            var reservationTasks = new List<Task<(ITag Tag, ResultBox<TagWriteReservation> Result)>>();
            
            // Get the latest sortable unique ID for each tag from accessed states
            var accessedStates = commandContext.GetAccessedTagStates();
            
            foreach (var tag in allTags)
            {
                var lastSortableUniqueId = "";
                
                // Check if this is a ConsistencyTag with a specific version requirement
                if (tag is ConsistencyTag consistencyTag && consistencyTag.SortableUniqueId.HasValue)
                {
                    // Use the specified SortableUniqueId for optimistic locking
                    lastSortableUniqueId = consistencyTag.SortableUniqueId.GetValue().Value;
                }
                else 
                {
                    // For ConsistencyTag without version, use the InnerTag to look up accessed state
                    var lookupTag = tag is ConsistencyTag ct ? ct.InnerTag : tag;
                    if (accessedStates.TryGetValue(lookupTag, out var state))
                    {
                        // Use the last accessed state's sortable unique ID
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
                }
                else
                {
                    failedReservations.Add((tag, result.GetException()));
                }
            }
            
            // If any reservations failed, cancel all successful reservations
            if (failedReservations.Any())
            {
                await CancelReservationsAsync(reservations, cancellationToken);
                
                var errorMessage = string.Join("; ", 
                    failedReservations.Select(f => $"Tag {f.Tag.GetTag()}: {f.Error.Message}"));
                return ResultBox.Error<ExecutionResult>(
                    new InvalidOperationException($"Failed to reserve tags: {errorMessage}"));
            }
            
            try
            {
                // Step 5: Write event to EventStore (handles both events and tags)
                var eventId = Guid.NewGuid();
                var sortableId = SortableUniqueId.GenerateNew();
                var metadata = new EventMetadata(
                    eventId.ToString(),
                    command.GetType().Name,
                    "GeneralSekibanExecutor"
                );
                
                var evt = new Event(
                    eventWithTags.Event,
                    sortableId,
                    eventWithTags.Event.GetType().Name,
                    eventId,
                    metadata,
                    eventWithTags.Tags.Select(t => t.GetTag()).ToList()
                );
                
                var events = new List<Event> { evt };
                
                var writeResult = await _eventStore.WriteEventsAsync(events);
                if (!writeResult.IsSuccess)
                {
                    await CancelReservationsAsync(reservations, cancellationToken);
                    return ResultBox.Error<ExecutionResult>(writeResult.GetException());
                }
                
                var (writtenEvents, tagWriteResults) = writeResult.GetValue();
                
                // Step 6: Confirm reservations with TagConsistentActors
                await ConfirmReservationsAsync(reservations, cancellationToken);
                
                // Return success result
                var firstEvent = writtenEvents.First();
                return ResultBox.FromValue(new ExecutionResult(
                    firstEvent.Id,
                    1, // TODO: Get actual event position
                    tagWriteResults.ToList(),
                    stopwatch.Elapsed,
                    new Dictionary<string, object>
                    {
                        ["EventCount"] = writtenEvents.Count,
                        ["TagCount"] = allTags.Count
                    }
                ));
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
        CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        // Delegate to the function-based implementation
        return await ExecuteAsync(command, handler.HandleAsync, cancellationToken);
    }
    
    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommandWithHandler<TCommand>
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
                return ResultBox.FromValue(new TagState(
                    new EmptyTagStatePayload(),
                    state.Version,
                    state.LastSortedUniqueId,
                    state.TagGroup,
                    state.TagContent,
                    state.TagProjector,
                    state.ProjectorVersion
                ));
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
            
            return ResultBox.FromValue(new TagState(
                payload,
                state.Version,
                state.LastSortedUniqueId,
                state.TagGroup,
                state.TagContent,
                state.TagProjector,
                state.ProjectorVersion
            ));
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