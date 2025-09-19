# DCB Interfaces

This document summarizes the primary interfaces in the current DCB (Dynamic Consistency Boundary) implementation.

## Event Interfaces

### `IEventPayload`
```csharp
public interface IEventPayload
{
}
```

a marker interface implemented by every domain event payload.

## Tag Interfaces

### `ITag`
```csharp
public interface ITag
{
    bool IsConsistencyTag();
    string GetTagGroup();
    string GetTagContent();
}
```

Tags identify the participants of a command and decide whether reservation is required (`IsConsistencyTag()`). The combination of group and content forms the unique tag id `"[group]:[content]"`.

### `ITagStatePayload`
```csharp
public interface ITagStatePayload
{
}
```

Marker interface for materialized tag state payloads.

### `ITagProjector<TProjector>`
```csharp
public interface ITagProjector<TProjector> where TProjector : ITagProjector<TProjector>
{
    static abstract string ProjectorVersion { get; }
    static abstract string ProjectorName { get; }
    static abstract ITagStatePayload Project(ITagStatePayload current, Event ev);
}
```

Projectors rebuild tag state by applying events. Static members provide the projector name/version that TagStateActors need for caching.

## Actor Interfaces

### `ITagStateActorCommon`
```csharp
public interface ITagStateActorCommon
{
    Task<SerializableTagState> GetStateAsync();
    Task<string> GetTagStateActorIdAsync();
}
```

Actors that materialize tag projections expose their current state and identifier through this interface.

### `ITagConsistentActorCommon`
```csharp
public interface ITagConsistentActorCommon
{
    Task<string> GetTagActorIdAsync();
    Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();
    Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId);
    Task<bool> ConfirmReservationAsync(TagWriteReservation reservation);
    Task<bool> CancelReservationAsync(TagWriteReservation reservation);
}
```

Actors responsible for tag-level consistency implement these methods to manage reservations and expose the latest committed SortableUniqueId.

### `IActorObjectAccessor`
```csharp
public interface IActorObjectAccessor
{
    Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class;
    Task<bool> ActorExistsAsync(string actorId);
}
```

The executor and command context rely on this abstraction to locate actors regardless of the hosting technology (in-memory, Orleans, Dapr, …).

## Command Interfaces

### `ICommand`
```csharp
public interface ICommand
{
}
```

Marker for command messages processed by the executor.

### `ICommandWithHandler<TCommand>`
```csharp
public interface ICommandWithHandler<in TCommand> : ICommand
    where TCommand : ICommandWithHandler<TCommand>
{
    static abstract Task<ResultBox<EventOrNone>> HandleAsync(TCommand command, ICommandContext context);
}
```

Optional helper for commands that expose a static handler.

### `ICommandContext`
```csharp
public interface ICommandContext
{
    Task<ResultBox<TagState>> GetStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector>;
    Task<ResultBox<TagStateTyped<TState>>> GetStateAsync<TState, TProjector>(ITag tag)
        where TState : ITagStatePayload
        where TProjector : ITagProjector<TProjector>;
    Task<ResultBox<bool>> TagExistsAsync(ITag tag);
    Task<ResultBox<string>> GetTagLatestSortableUniqueIdAsync(ITag tag);
    ResultBox<EventOrNone> AppendEvent(EventPayloadWithTags ev);
}
```

The context exposes projection queries, optimistic-concurrency helpers, and event collection APIs to command handlers.

### `ICommandContextResultAccessor`
```csharp
public interface ICommandContextResultAccessor
{
    IReadOnlyList<EventPayloadWithTags> GetAppendedEvents();
    IReadOnlyDictionary<ITag, TagState> GetAccessedTagStates();
    void ClearResults();
}
```

The executor inspects the context through this interface after the handler completes.

### `ICommandExecutor`
```csharp
public interface ICommandExecutor
{
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommandWithHandler<TCommand>;
}
```

The executor orchestrates command validation, reservation, persistence, and optional publication.
