# Commands, Events, Tags, Projectors - Sekiban DCB

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md) (You are here)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

DCB replaces the aggregate-centric vocabulary with a tag-centric one. Commands still describe the user intent, but they
produce a single event that targets multiple tags. Projectors rebuild tag state per tag instead of per aggregate.

## Commands

Implement commands as records with validation attributes and a static handler. The handler receives an
`ICommandContext`, which exposes tag state queries, optimistic concurrency helpers, and event append helpers.

```csharp
public record CreateStudent : ICommandWithHandler<CreateStudent>
{
    [Required] public Guid StudentId { get; init; }
    [Required] public string Name { get; init; } = default!;
    [Range(1, 10)] public int MaxClassCount { get; init; } = 5;

    public static Task<ResultBox<EventOrNone>> HandleAsync(CreateStudent command, ICommandContext context) =>
        ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(context.TagExistsAsync)
            .Verify((_, exists) => exists
                ? ExceptionOrNone.FromException(new ApplicationException("Student Already Exists"))
                : ExceptionOrNone.None)
            .Conveyor((tag, _) => EventOrNone.EventWithTags(
                new StudentCreated(command.StudentId, command.Name, command.MaxClassCount),
                tag));
}
// Source: internalUsages/Dcb.Domain/Student/CreateStudent.cs
```

## Event Payloads

Events are immutable records implementing `IEventPayload`. Each command returns exactly one payload representing the full
business fact. For shared operations (e.g., enrollment) the event references every participant.

```csharp
public record StudentEnrolledInClassRoom(Guid StudentId, Guid ClassRoomId) : IEventPayload;
// Source: internalUsages/Dcb.Domain/Enrollment/StudentEnrolledInClassRoom.cs
```

## Tags

Tags describe which entities the event touches. They implement `ITag` or helper interfaces like `IGuidTagGroup<T>` that
provide consistent formatting. Tags can opt-in to reservation by returning `true` from `IsConsistencyTag()`.

```csharp
public record StudentTag(Guid StudentId) : IGuidTagGroup<StudentTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "Student";
    public static StudentTag FromContent(string content) => new(Guid.Parse(content));
    public Guid GetId() => StudentId;
}
// Source: internalUsages/Dcb.Domain/Student/StudentTag.cs
```

Use helper tags for secondary dimensions. In the sample domain `YearlyStudentsTag` aggregates statistics by year but
returns `false` for `IsConsistencyTag()` so it never blocks writes.

### First-write reservation semantics (SEK-G19)

A write to a consistency tag reserves it with an **expected version** (the tag's last `SortableUniqueId`), compared
inside the actor as an **exact match after null/empty normalization**:

- An **empty** expected version means "**I expect this tag to be empty**" — a first write. It succeeds only when the tag
  has no committed state; a second, non-overlapping first write against a tag that already has state **conflicts**
  (surfaced through the existing `ResultBox.Error` channel — there is no new public exception type).
- A **non-empty** expected version must match the tag's current version exactly. A non-empty expectation against an empty
  tag, or a mismatch, conflicts; an exact match is an ordinary update.

How the expected version is chosen for a write:

- If the command **accessed the tag state** (`GetStateAsync`) or confirmed it **exists** (`TagExistsAsync`), the write
  reserves on the tag's **current** version — an update.
- Otherwise the write reserves **expect-empty** — a first write. So a command that only emits an event for a tag it never
  read is treated as a create, and a second such create conflicts.
- A `ConsistencyTag.FromTagWithSortableUniqueId(...)` supplies an explicit expected version used verbatim.

**Guarantee boundary (per cluster).** With Orleans this holds **at-most-one first write PER CLUSTER** — one actor
activation per tag serialises reservations. Independent clusters do **not** coordinate through the actor: two clusters can
each reserve-empty and durably append a duplicate create for the same tag. Cross-cluster uniqueness is the **storage
layer's** job — the conditional unique-append contract (see [Storage Providers](11_storage_providers.md)); convergence
over any durable duplicates is handled by the multi-projection layer (SEK-G18). Applications that need a hard "this ID
already exists" guarantee across clusters must rely on storage unique-append, not the actor reservation.

**Behavior change**: before 10.8.0 an empty expected version skipped the check, so one side of a racing create could
silently succeed. From 10.8.0 that side now fails with a consistency error — create projectors that were written to
tolerate duplicate first writes on a single cluster can retire that workaround.

### Shared-store stale-empty re-check (SEK-G22 / 10.8.2)

In a multi-cluster deployment, an actor can have a successfully cached empty tag while a command fold already sees a
write committed by another cluster. When that fold supplies the committed non-empty version, 10.8.0's exact-match check
would compare it with the stale empty cache and falsely reject the update.

From 10.8.2, that single anomalous shape — non-empty expected version with an empty actor cache — performs one bounded
authoritative tag read while holding the reservation lock. A matching version proceeds; an authoritative empty or a
different version remains a consistency conflict; a read failure fails closed. Successful reads are adopted before the
final comparison, so a later expect-empty request cannot pass against known durable state. Normal matches, mismatches,
and every empty-expected path perform no extra read.

This is a false-rejection fix, not cross-cluster uniqueness. Storage conditional unique-append (G15/G16) remains the
authority for preventing duplicate writes across clusters; no API, schema, default, or migration changes in 10.8.2.

## Tag State Payloads

Projectors rebuild tag state into `ITagStatePayload` records. Keep them small and immutable.

```csharp
[GenerateSerializer]
public record StudentState(Guid StudentId, string Name, int MaxClassCount, List<Guid> EnrolledClassRoomIds)
    : ITagStatePayload
{
    public int GetRemaining() => MaxClassCount - EnrolledClassRoomIds.Count;
}
// Source: internalUsages/Dcb.Domain/Student/StudentState.cs
```

## Tag Projectors

Tag projectors are static classes that implement `ITagProjector<T>`. They receive the current payload (or
`EmptyTagStatePayload`) and the incoming event. Always return the new payload without mutating shared state.

```csharp
public class StudentProjector : ITagProjector<StudentProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(StudentProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev) => (current, ev.Payload) switch
    {
        (EmptyTagStatePayload, StudentCreated created) => new StudentState(
            created.StudentId,
            created.Name,
            created.MaxClassCount,
            new List<Guid>()),

        (StudentState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 0 => state with
        {
            EnrolledClassRoomIds = state.EnrolledClassRoomIds
                .Concat(new[] { enrolled.ClassRoomId })
                .Distinct()
                .ToList()
        },

        (StudentState state, StudentDroppedFromClassRoom dropped) => state with
        {
            EnrolledClassRoomIds = state.EnrolledClassRoomIds
                .Where(id => id != dropped.ClassRoomId)
                .ToList()
        },

        _ => current
    };
}
// Source: internalUsages/Dcb.Domain/Student/StudentProjector.cs
```

Projector versioning lets you force a rebuild when the projection logic changes. Actors compare the version string before
reusing cached state.

## Multi-Tag Commands

Commands that span multiple tags combine states before emitting the business fact. The executor automatically merges tags
from the returned `EventOrNone` plus any additional events appended in the context.

```csharp
public class EnrollStudentInClassRoomHandler : ICommandHandler<EnrollStudentInClassRoom>
{
    public static Task<ResultBox<EventOrNone>> HandleAsync(EnrollStudentInClassRoom command, ICommandContext context) =>
        ResultBox.Start
            .Remap(_ => new StudentTag(command.StudentId))
            .Combine(context.GetStateAsync<StudentState, StudentProjector>)
            .Verify((_, studentState) => studentState.Payload.GetRemaining() <= 0
                ? ExceptionOrNone.FromException(new("Student has reached maximum class count"))
                : studentState.Payload.EnrolledClassRoomIds.Contains(command.ClassRoomId)
                    ? ExceptionOrNone.FromException(new("Student is already enrolled in this classroom"))
                    : ExceptionOrNone.None)
            .Remap((studentTag, _) => TwoValues.FromValues(studentTag, new ClassRoomTag(command.ClassRoomId)))
            .Combine((_, classRoomTag) => context.GetStateAsync<ClassRoomProjector>(classRoomTag))
            .Verify((_, _, classRoomState) => classRoomState.Payload switch
            {
                AvailableClassRoomState available when available.GetRemaining() <= 0 =>
                    ExceptionOrNone.FromException(new("ClassRoom is full")),
                AvailableClassRoomState available when available.EnrolledStudentIds.Contains(command.StudentId) =>
                    ExceptionOrNone.FromException(new("Student is already enrolled in this classroom")),
                FilledClassRoomState => ExceptionOrNone.FromException(new("ClassRoom is full")),
                _ => ExceptionOrNone.None
            })
            .Conveyor((studentTag, classRoomTag, _) => EventOrNone.EventWithTags(
                new StudentEnrolledInClassRoom(command.StudentId, command.ClassRoomId),
                studentTag,
                classRoomTag));
}
// Source: internalUsages/Dcb.Domain/Enrollment/EnrollStudentInClassRoomHandler.cs
```

## Recording the executing user (SEK-G23)

Every event carries `EventMetadata.ExecutedUser`. By default the command path writes the literal `"GeneralSekibanExecutor"`,
and the serialized/WASM commit path writes `"SerializedSekibanExecutor"`.

To record the real caller, implement `IExecutedUserProvider` and register it in DI. The canonical pattern is a singleton
provider that reads the ambient HTTP context on every call:

```csharp
public class HttpContextExecutedUserProvider : IExecutedUserProvider
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextExecutedUserProvider(IHttpContextAccessor accessor) => _accessor = accessor;
    public string GetExecutedUser() => _accessor.HttpContext?.User.Identity?.Name ?? "anonymous";
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IExecutedUserProvider>(sp =>
    new HttpContextExecutedUserProvider(sp.GetRequiredService<IHttpContextAccessor>()));
```

The provider is evaluated exactly once per command and the captured value is reused for every event that command emits.
If the provider is absent or returns `null`/empty, the value falls back to `"GeneralSekibanExecutor"`.

> **Lifetime guidance.** `IExecutedUserProvider` is captured by the executor. If your provider is scoped or transient,
> register the executor as scoped or transient as well so it does not hold a stale provider. The ambient HTTP-context
> pattern above keeps the provider singleton, so the executor may also be singleton.

Constructor parameters for the provider are optional on all executor facades (`GeneralSekibanExecutor`,
`OrleansDcbExecutor`, `InMemoryDcbExecutor`, and their WithoutResult/testing variants), so existing call sites are
unchanged.

## Tips

- Use helper classes such as `ConsistencyTag` when you need to carry a known `SortableUniqueId` across retries.
- Prefer small, focused tag payloads. Move aggregations into MultiProjection so TagState actors stay lean.
- Commands should never mutate state directly—always delegate to events recorded through the executor.
