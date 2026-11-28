# Migrating to DCB 11

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Storage Providers](11_storage_providers.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Migrating to DCB 11](24_migrating_to_11.md) (You are here)

This page covers the move from DCB 10.x (10.25.0) to the DCB 11 line, starting with `11.0.1-preview01`.

In 11, `ResultBox` belongs to the WithResult surface only. Four packages depend on `ResultBoxes`:
`Sekiban.Dcb.WithResult`, `Sekiban.Dcb.WithResult.Model`, `Sekiban.Dcb.Orleans.WithResult` and
`Sekiban.Dcb.WithResult.Testing`. Core, Core.Model, Core.Testing, every storage provider, Orleans.Core,
MaterializedView and the WithoutResult packages are exception based.

The conversion follows four rules:

1. **Failures are exceptions of the same type and message as before.** What used to sit in a failed `ResultBox` is now
   thrown.
2. **Expected outcomes are values.** "Not found", "no events" and "nothing to delete" are `null`, `false`, an empty
   string or an empty state, not errors.
3. **Cancellation stays `OperationCanceledException`.**
4. **Contracts change shape only.** `Task<ResultBox<T>>` becomes `Task<T>` and `ResultBox<T>` becomes `T`; member names
   and parameters stay the same.

Event data, tag data, snapshots and persisted Orleans grain state did not change. No data migration is required.

## Who needs to read what

| You... | Read |
|---|---|
| write commands, projectors and queries with `Sekiban.Dcb.WithResult` | [WithResult users](#withresult-users), [Rebuild and dependencies](#rebuild-and-dependencies) |
| write them with `Sekiban.Dcb.WithoutResult` | [WithoutResult users](#withoutresult-users), [Rebuild and dependencies](#rebuild-and-dependencies) |
| implement `IEventStore` or another storage contract | [Custom storage providers](#custom-storage-providers) |
| implement `ICoreMultiProjectorTypes`, `ITagProjectorTypes`, `ITagStatePayloadTypes`, `ICoreQueryTypes` or call the core contracts directly | [Custom `I*Types` and core contract implementers](#custom-itypes-and-core-contract-implementers), [Core runtime contracts](#core-runtime-contracts) |
| run Orleans | [Orleans](#orleans) |
| derive from `MvExecutorBase` | [MaterializedView](#materializedview) |
| everyone | [Behaviour changes beyond shape](#behaviour-changes-beyond-shape) |

## Rebuild and dependencies

- **Targets.** The 11 packages target `net10.0` and `net11.0`. On `net10.0` the dependencies are the same versions as
  10.25.0, except `ResultBoxes` (see below). On `net11.0`, `Microsoft.Extensions.*`, `Microsoft.Data.Sqlite`, Entity
  Framework Core, `Npgsql` and `Npgsql.EntityFrameworkCore.PostgreSQL` are `11.0.0` (all GA). If a `net11.0` project
  references `Microsoft.Data.Sqlite`, Entity Framework Core, `Npgsql` or `Npgsql.EntityFrameworkCore.PostgreSQL`
  directly at 10.x, update it to 11.0.0 or later; otherwise NuGet reports a package downgrade (NU1605).
  `Microsoft.Orleans.*` is 10.4.2 on both.
- **Rebuild everything.** Assemblies compiled against 10.x are not binary compatible with 11. Rebuild every project that
  references a `Sekiban.Dcb.*` package. For example, `EventOrNone.None`, `EventOrNone.Event(...)` and
  `EventOrNone.EventWithTags(...)` moved from `EventOrNone` to C# extension members in `Sekiban.Dcb.WithResult.Model`
  (source compatible, binary incompatible), and a domain registry compiled against an old Sekiban version fails to load
  with `TypeLoadException`.
- **`ResultBoxes` is no longer a transitive dependency of WithoutResult.** If your WithoutResult application uses
  `ResultBox` types itself (for example in a domain project), reference the package directly:

  ```xml
  <PackageReference Include="ResultBoxes" Version="0.4.1" />
  ```

  The WithoutResult and Decider DCB templates do not use `ResultBox` and no longer reference `ResultBoxes`.
- **`ResultBoxes` 0.4.1.** The WithResult packages depend on `ResultBoxes` 0.4.1 (10.25.x used 0.4.0; 0.4.1 adds a
  net11.0 build and has the same API). If your project references `ResultBoxes` 0.4.0 directly, as projects made from
  the 10.x Orleans template do, update it to 0.4.1; otherwise NuGet reports a package downgrade (NU1605).

## WithResult users

The WithResult authoring API is unchanged. Command handlers still return `Task<ResultBox<EventOrNone>>`,
`IMultiProjector<T>.Project` still returns `ResultBox<T>`, `IMultiProjectionQuery` / `IMultiProjectionListQuery`
handlers still return boxes, `ICommandContext` still returns boxes, and `ISekibanExecutor` (including the Orleans
executor) still returns boxes. `EventOrNone.None`, `.Event(...)`, `.EventWithTags(...)` and
`SortableUniqueId.OptionalValue(...)` still compile, now as extension members from `Sekiban.Dcb.WithResult.Model`.

The WithResult interfaces implement the exception-based core members explicitly by unwrapping your box once, so a failed
box reaches the core as the exception it carried, and the WithResult facade boxes core failures with the original
exception.

**A failed box may now carry the original exception type.** In 10.x, a failure returned by the grain members that
returned boxes came back flattened to `Exception("[TypeName] message")` (a `SekibanProjectionFaultException` was the
exception: it kept its type and descriptor). In 11 those members send the exception itself wherever Orleans can
serialize it (see [Exception transport](#exception-transport)). Through `OrleansDcbExecutor` this shows in two places:

- `ICommandContext.TagExistsAsync` and `ICommandContext.GetTagLatestSortableUniqueIdAsync` read
  `ITagConsistentGrain.GetLatestSortableUniqueIdAsync`. A failure raised inside the grain now reaches your handler's
  failed box with its real type and message instead of `Exception("[TypeName] message")`, for example
  `InvalidOperationException("Actor not initialized")` or a failure while waiting for the actor's lock. (WithoutResult
  throws the same exception with its boundary annotations.) Call failures such as Orleans timeouts or transport errors
  are raised on the caller side and behave as in 10.x, and the tag actor's catch-up swallows event-store failures, as
  in 10.x, so neither changes on this path.
- Tag reservations call `ITagConsistentGrain.MakeReservationAsync`. Their failures are still folded into
  `InvalidOperationException("Failed to reserve tags: ...")`; only the per-tag text inside it may no longer start with
  `[TypeName]`.

Code that matched on the `[TypeName]` prefix must match on the exception type, or stop relying on the prefix. Code
that calls the grains directly gets a faulted task carrying the exception (see [Orleans](#orleans)).

Source breaks that can still reach WithResult code:

- `SortableUniqueId.Parse(string)` returns `SortableUniqueId` and throws `ArgumentException` (same messages);
  `TryParse` is unchanged.
- `ListQueryResultGeneral.ToTypedResult<T>()` returns `ListQueryResult<T>`, and `QueryResultGeneral.ToTypedResult<T>()`
  returns `T`.
- `ConsistencyTag.SortableUniqueId` is `SortableUniqueId?` (was `OptionalValue<SortableUniqueId>`).
- `ICommandContext` no longer derives from `ICoreCommandContext`, and `GeneralCommandContext` no longer implements it.
- Members that `SimpleMultiProjectorTypes` inherits from the core registry (`Project`, `GetProjectorVersion`,
  `GetProjectorType`, `GenerateInitialPayload`, `GetInitialPayloadGenerator`, `Serialize`, `SerializeToStream`,
  `Deserialize`, `DeserializeFromStreamAsync`) return values. `RegisterProjectorWithCustomSerialization<T>()` on the
  WithResult `SimpleMultiProjectorTypes` still returns `ResultBox<bool>`.
- Code that calls a core contract directly ([Custom `I*Types`](#custom-itypes-and-core-contract-implementers),
  [Core runtime contracts](#core-runtime-contracts), [Custom storage providers](#custom-storage-providers)) gets values,
  even when the object is a WithResult type. For example, `GeneralSekibanExecutor` (WithResult) still exposes
  `Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(...)`, but called through
  `ISerializedSekibanDcbExecutor` it returns `Task<SerializedCommitResult>`.

## WithoutResult users

Command handlers, projectors, queries and `ISekibanExecutor` keep their 10.x shape. The changes are:

- **Serialized executor members** return values instead of boxes. On `GeneralSekibanExecutor`
  (`Sekiban.Dcb.Actors`, Sekiban.Dcb.WithoutResult):

  ```csharp
  // 10.x
  public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId)
  public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
      SerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
      VersionedExpectedTagPositionSerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
      SerializedConditionalCommitRequest request,
      CancellationToken cancellationToken = default)

  // 11
  public Task<SerializableTagState> GetSerializableTagStateAsync(TagStateId tagStateId)
  public Task<SerializedCommitResult> CommitSerializableEventsAsync(
      SerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<SerializedCommitResult> CommitSerializableEventsWithExpectedTagPositionsAsync(
      VersionedExpectedTagPositionSerializedCommitRequest request,
      CancellationToken cancellationToken = default)
  public Task<SerializedConditionalCommitResult> CommitSerializableEventConditionallyAsync(
      SerializedConditionalCommitRequest request,
      CancellationToken cancellationToken = default)
  ```

  `OrleansDcbExecutor` (Sekiban.Dcb.Orleans.WithoutResult) changes the first three the same way. Failures throw with
  the same boundary annotations as every other WithoutResult member.
- **`SimpleMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>()` returns `bool`** (was `ResultBox<bool>`).
  It returns `true`, also when the projector is already registered; a failure throws the original exception (10.x
  returned a failed box that callers usually ignored). `AotWithoutResultMultiProjectorTypes` returns `bool` and throws
  `NotSupportedException("Use RegisterProjector with JsonTypeInfo")`.
- **The boundary guard.** The internal `GuardedUnwrap` became `BoundaryGuard`; it no longer opens boxes, because Core
  throws. A failure is rethrown as itself (same instance, type and stack), and the boundary is recorded in
  `Exception.Data` under the unchanged keys `Sekiban.Boundary.Operation` and `Sekiban.Boundary.Target`. A null `Task` or
  null result is still a `SekibanBoundaryException`. See [Common Issues](13_common_issues.md).
- **`ResultBoxes` is not brought in any more**, see [Rebuild and dependencies](#rebuild-and-dependencies).
- **Assembly-scan registration errors** surface as the original exception instead of `TargetInvocationException`.

## Custom storage providers

Every storage contract follows the four rules. Failures throw the exception your provider used to put in the failed
box. Do not catch and box; let `OperationCanceledException` propagate.

### Not-found and empty results

| Member | 11 result when there is nothing |
|---|---|
| `IEventStore.ReadSerializableEventAsync` | `null` (10.x: a failed box with a provider-specific exception) |
| `IEventStore.TagExistsAsync` | `false` |
| `IEventStore.GetLatestTagAsync` | an empty `TagState` (version 0) |
| `IEventStore.GetLatestSortableUniqueIdAsync` | `""` |
| `IMultiProjectionStateStore.GetLatestForVersionAsync` / `GetLatestAnyVersionAsync` | `null` (10.x: an empty `OptionalValue`) |
| `IMultiProjectionStateStore.DeleteAsync` | `false` when nothing was deleted |
| `IMultiProjectionStateStore.DeleteAllAsync` | the number deleted (`0`) |
| `IGenerationAwareCheckpointStore.ReadCheckpointSlotAsync` | `CheckpointSlot.Absent` |
| `IColdObjectStorage.GetAsync` | `null`, exactly where the provider used to report not-found |
| `IColdObjectStorage.DeleteAsync` | `false` when no object existed |
| `IColdObjectStorage.OpenReadAsync` | throws; the default implementation throws `KeyNotFoundException` |

A rejected compare-and-set stays a value: `ProjectionStatusWriteResult` from `IProjectionStatusStore.UpsertAsync`, and
`CheckpointCasOutcome` from the checkpoint CAS members (those already returned values in 10.x).

### `IEventStore` (Sekiban.Dcb.Storage)

```csharp
// 10.x
Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag);
Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag);
Task<ResultBox<bool>> TagExistsAsync(ITag tag);
Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null);
Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount);
Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId);
Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
    IEnumerable<SerializableEvent> events);
Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();

// 11
Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag);
Task<TagState> GetLatestTagAsync(ITag tag);
Task<bool> TagExistsAsync(ITag tag);
Task<long> GetEventCountAsync(SortableUniqueId? since = null);
Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null);
Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);
Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount);
Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId);
Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
    IEnumerable<SerializableEvent> events);
Task<string> GetLatestSortableUniqueIdAsync();
```

The `EventStoreExtensions` helpers follow: `ReadAllEventsAsync` and `ReadEventsByTagAsync` return
`Task<IEnumerable<Event>>`, `ReadEventAsync` returns `Task<Event?>` (`null` when the event does not exist),
`WriteEventsAsync` returns `Task<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>` and
`WriteEventAsync` returns `Task<Event>`. `EventSerializationExtensions.ToEvent` returns `Event` and throws on failure.

### `IMultiProjectionStateStore` (Sekiban.Dcb.Storage)

```csharp
// 10.x
Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
    string projectorName, string projectorVersion, CancellationToken cancellationToken = default);
Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
    string projectorName, CancellationToken cancellationToken = default);
Task<ResultBox<bool>> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000,
    CancellationToken cancellationToken = default);
Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(CancellationToken cancellationToken = default);
Task<ResultBox<bool>> DeleteAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
Task<ResultBox<int>> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default);
Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record,
    CancellationToken cancellationToken = default);
Task<ResultBox<bool>> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream,
    int offloadThresholdBytes, CancellationToken cancellationToken = default);

// 11
Task<MultiProjectionStateRecord?> GetLatestForVersionAsync(
    string projectorName, string projectorVersion, CancellationToken cancellationToken = default);
Task<MultiProjectionStateRecord?> GetLatestAnyVersionAsync(
    string projectorName, CancellationToken cancellationToken = default);
Task<bool> UpsertAsync(MultiProjectionStateRecord record, int offloadThresholdBytes = 1_000_000,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<ProjectorStateInfo>> ListAllAsync(CancellationToken cancellationToken = default);
Task<bool> DeleteAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
Task<int> DeleteAllAsync(string? projectorName = null, CancellationToken cancellationToken = default);
Task<Stream> OpenStateDataReadStreamAsync(MultiProjectionStateRecord record,
    CancellationToken cancellationToken = default);
Task<bool> UpsertFromStreamAsync(MultiProjectionStateWriteRequest request, Stream stream,
    int offloadThresholdBytes, CancellationToken cancellationToken = default);
```

The default bodies of `OpenStateDataReadStreamAsync` and `UpsertFromStreamAsync` now return a faulted task with
`NotSupportedException` (10.x: a failed box with the same exception). Other failures throw the exception types they
used to carry.

### Status and checkpoint stores

```csharp
// IProjectionStatusStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence,
    CancellationToken cancellationToken = default);
Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(string? projectorName = null,
    string? projectorVersion = null, CancellationToken cancellationToken = default);
// 11 (the convenience UpsertAsync overload and ListAllAsync change the same way)
Task<ProjectionStatusWriteResult> UpsertAsync(ProjectionStatusHeartbeat heartbeat, long expectedSequence,
    CancellationToken cancellationToken = default);
Task<IReadOnlyList<ProjectionStatusHeartbeat>> ListAsync(string? projectorName = null,
    string? projectorVersion = null, CancellationToken cancellationToken = default);

// IGenerationAwareCheckpointStore (Sekiban.Dcb.Storage.Checkpoints)
// 10.x
Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
// 11 (CheckpointSlot.Absent when no row exists; the CAS members are unchanged)
Task<CheckpointSlot> ReadCheckpointSlotAsync(string projectorName, string projectorVersion,
    CancellationToken cancellationToken = default);
```

### Conditional and expected-tag-position stores

```csharp
// IConditionalEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(ConditionalAppendRequest request,
    CancellationToken cancellationToken = default);
// 11
Task<ConditionalAppendReceipt> AppendIfUniqueAsync(ConditionalAppendRequest request,
    CancellationToken cancellationToken = default);

// IExpectedTagPositionEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken cancellationToken = default);
Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
    IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification,
    CancellationToken cancellationToken = default);
// 11
Task<bool> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken cancellationToken = default);
Task<ExpectedTagPositionWriteResult> WriteSerializableEventsWithExpectedTagPositionsAsync(
    IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification,
    CancellationToken cancellationToken = default);
```

`Appended` and `AlreadyCommittedSameOperation` are receipts. A different operation on the same key throws
`KeyReuseConflictException`; an unresolved outcome throws `ConditionalAppendInDoubtException`; expected-position
conflicts and validation failures throw `ExpectedTagPositionConflictException` and
`TagHeadExpectationValidationException`. These are the exception types that 10.x carried in the failed box.
`ConditionalAppendCoordinator` and `ConditionalAppendExecution` follow the same shape.

### Streaming

```csharp
// IStreamingSerializableEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<SerializableEventStreamReadResult>> StreamAllSerializableEventsAsync(SortableUniqueId? since,
    int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken cancellationToken = default);
// 11
Task<SerializableEventStreamReadResult> StreamAllSerializableEventsAsync(SortableUniqueId? since,
    int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken cancellationToken = default);

// IStreamingTaggedSerializableEventStore (Sekiban.Dcb.Storage)
// 10.x
Task<ResultBox<SerializableEventStreamReadResult>> StreamSerializableEventsByTagAsync(ITag tag,
    SortableUniqueId? since, SortableUniqueId? until, Func<SerializableEvent, ValueTask> onEvent,
    CancellationToken cancellationToken = default);
// 11
Task<SerializableEventStreamReadResult> StreamSerializableEventsByTagAsync(ITag tag,
    SortableUniqueId? since, SortableUniqueId? until, Func<SerializableEvent, ValueTask> onEvent,
    CancellationToken cancellationToken = default);
```

Throw non-cancellation failures and let cancellation propagate. An exception thrown by the caller's `onEvent` callback
must propagate unchanged.

### Cold storage (Sekiban.Dcb.ColdEvents)

```csharp
// IColdObjectStorage
// 10.x
Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct);
Task<ResultBox<Stream>> OpenReadAsync(string path, CancellationToken ct);
Task<ResultBox<bool>> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct);
Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct);
Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct);
Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct);
// 11
Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct);
Task<Stream> OpenReadAsync(string path, CancellationToken ct);
Task<bool> PutAsync(string path, Stream data, string? expectedETag, CancellationToken ct);
Task<bool> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct);
Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);
Task<bool> DeleteAsync(string path, CancellationToken ct);
```

The other cold contracts change shape the same way:

| Interface | 11 member |
|---|---|
| `IColdSegmentFormatHandler` | `Task<ColdSegmentStreamResult> StreamSegmentAsync(Stream data, SortableUniqueId? since, int? maxCount, Func<SerializableEvent, ValueTask> onEvent, CancellationToken ct)` |
| `IColdLeaseManager` | `Task<ColdLease> AcquireAsync(...)`, `Task<ColdLease> RenewAsync(...)`, `Task<bool> ReleaseAsync(...)` |
| `IColdEventProgressReader` | `Task<ColdStoreProgress> GetProgressAsync(string serviceId, CancellationToken ct)` |
| `IColdEventCatalogReader` | `Task<ColdDataRangeSummary> GetDataRangeSummaryAsync(string serviceId, CancellationToken ct)` |
| `IColdEventExporter` | `Task<ExportResult> ExportIncrementalAsync(string serviceId, CancellationToken ct)` |

A lease that cannot be acquired, renewed or released throws (the built-in `StorageBackedColdLeaseManager` throws
`InvalidOperationException`), as its failed box did in 10.x.

### Durable subscription store (Sekiban.Dcb.Subscriptions)

All ten members of `IDurableSubscriptionStore` drop the box; parameters are unchanged:

```csharp
// 10.x → 11
Task<ResultBox<DurableSubscriptionState>> InitializeOrGetAsync(...)  → Task<DurableSubscriptionState> InitializeOrGetAsync(...)
Task<ResultBox<DurableSubscriptionState>> ReadAsync(...)             → Task<DurableSubscriptionState> ReadAsync(...)
Task<ResultBox<DurableSubscriptionLease>> TryAcquireAsync(...)       → Task<DurableSubscriptionLease> TryAcquireAsync(...)
Task<ResultBox<bool>> RenewAsync(...)                                → Task<bool> RenewAsync(...)
Task<ResultBox<bool>> IsRetryDueAsync(...)                           → Task<bool> IsRetryDueAsync(...)
Task<ResultBox<DurableSubscriptionState>> AcknowledgeAsync(...)      → Task<DurableSubscriptionState> AcknowledgeAsync(...)
Task<ResultBox<DurableSubscriptionState>> RecordFailureAsync(...)    → Task<DurableSubscriptionState> RecordFailureAsync(...)
Task<ResultBox<DurableSubscriptionState>> ResumeAsync(...)           → Task<DurableSubscriptionState> ResumeAsync(...)
Task<ResultBox<DurableSubscriptionState>> HaltAsync(...)             → Task<DurableSubscriptionState> HaltAsync(...)
Task<ResultBox<DurableSubscriptionState>> MarkIdleAsync(...)         → Task<DurableSubscriptionState> MarkIdleAsync(...)
```

`DurableSubscriptionRunner` logs and handles store failures at the same points as in 10.x.

### Built-in provider classes

The public typed members of the built-in stores changed with the contracts: `InMemoryEventStore`,
`InMemoryMultiProjectionStateStore`, `PostgresEventStore`, `PostgresMultiProjectionStateStore`,
`PostgresDurableSubscriptionStore`, `CosmosDbEventStore`, `CosmosMultiProjectionStateStore`, `DynamoDbEventStore`,
`DynamoMultiProjectionStateStore`, `SqliteEventStore`, `SqliteMultiProjectionStateStore`, the cold object storages
(JSONL, SQLite, DuckDB, Azure Blob database, `AzureBlobColdObjectStorage`, `S3ColdObjectStorage`) and the Core.Testing
stores (`InMemoryConditionalEventStore`, `DelegatingCheckpointStore`, `DelegatingMultiProjectionStateStore`). The SQLite
`TagEventService`, `TagListService` and `TagStateService` delegate to the Core services and return values.

## Custom `I*Types` and core contract implementers

The registries and dispatchers in Core.Model (namespace `Sekiban.Dcb.Domains` unless stated) return values. A missing
name is a configuration error and throws the same exception type and message the failed box used to carry. Expected
lookups stay values.

```csharp
// ICoreMultiProjectorTypes (10.x returned ResultBox<...> for each of these)
IMultiProjectionPayload Project(string multiProjectorName, IMultiProjectionPayload payload, Event ev,
    List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold);
string GetProjectorVersion(string multiProjectorName);
Func<IMultiProjectionPayload> GetInitialPayloadGenerator(string multiProjectorName);
Type GetProjectorType(string multiProjectorName);
IMultiProjectionPayload GenerateInitialPayload(string multiProjectorName);
IMultiProjectionPayload Deserialize(byte[] data, string multiProjectorName, JsonSerializerOptions jsonOptions);
SerializationResult Serialize(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    IMultiProjectionPayload payload);
SerializationSizeInfo SerializeToStream(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    IMultiProjectionPayload payload, Stream destination);
IMultiProjectionPayload Deserialize(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold,
    byte[] data);
IMultiProjectionPayload DeserializeJson(string projectorName, string json, DcbDomainTypes domainTypes);
bool RegisterProjectorWithCustomSerialization<T>()
    where T : ICoreMultiProjectorWithCustomSerialization<T>, new();

// IStreamingMultiProjectorTypes
Task<IMultiProjectionPayload> DeserializeFromStreamAsync(string projectorName, DcbDomainTypes domainTypes,
    string safeWindowThreshold, Stream source, CancellationToken cancellationToken = default);

// ITagProjectorTypes
Func<ITagStatePayload, Event, ITagStatePayload> GetProjectorFunction(string tagProjectorName);
string GetProjectorVersion(string tagProjectorName);
string? TryGetProjectorForTagGroup(string tagGroupName);   // unchanged: null when none

// ITagStatePayloadTypes
string GetPayloadName(ITagStatePayload payload);
Type? GetPayloadType(string payloadName);                  // unchanged: null when unknown
ITagStatePayload DeserializePayload(string payloadName, byte[] jsonBytes);
byte[] SerializePayload(ITagStatePayload payload);

// ICoreQueryTypes (Sekiban.Dcb.Queries); projectorProvider was Func<Task<ResultBox<IMultiProjectionPayload>>>
Task<object> ExecuteQueryAsync(IQueryCommon query, Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider, int? safeVersion = null, string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Task<object> ExecuteListQueryAsync(IListQueryCommon query, Func<Task<IMultiProjectionPayload>> projectorProvider,
    IServiceProvider serviceProvider, int? safeVersion = null, string? safeWindowThreshold = null,
    DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Task<ListQueryResultGeneral> ExecuteListQueryAsGeneralAsync(IListQueryCommon query,
    Func<Task<IMultiProjectionPayload>> projectorProvider, IServiceProvider serviceProvider, int? safeVersion = null,
    string? safeWindowThreshold = null, DateTime? safeWindowThresholdTime = null, int? unsafeVersion = null);
Type GetMultiProjectorType(IQueryCommon query);
Type GetMultiProjectorType(IListQueryCommon query);
```

The default bodies of `SerializeToStream` and `DeserializeJson` throw instead of returning a failed box. The built-in
`SimpleTagProjectorTypes`, `SimpleTagStatePayloadTypes`, `SimpleQueryTypes`, the `Aot*Types` classes and
`ProjectionHeadStatusUtilities` follow the same shape. A custom `IQueryTypes` must not return `null`: the Orleans query
executor fails with `InvalidOperationException("Query X completed without a result.")`.

The static core contracts return plain values:

```csharp
// ICoreMultiProjector<T> (Sekiban.Dcb.MultiProjections); was static abstract ResultBox<T> Project(...)
static abstract T Project(T payload, Event ev, List<ITag> tags, DcbDomainTypes domainTypes,
    SortableUniqueId safeWindowThreshold);

// ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput> (Sekiban.Dcb.Queries)
static abstract TOutput HandleQuery(TMultiProjector projector, TQuery query, IQueryContext context);

// ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> (Sekiban.Dcb.Queries)
static abstract IEnumerable<TOutput> HandleFilter(TMultiProjector projector, TQuery query, IQueryContext context);
static abstract IEnumerable<TOutput> HandleSort(IEnumerable<TOutput> filteredList, TQuery query, IQueryContext context);

// ICoreCommandContext (Sekiban.Dcb.Commands); each member was Task<ResultBox<...>>
Task<TagStateTyped<TState>> GetStateAsync<TState, TProjector>(ITag tag) where TState : ITagStatePayload
    where TProjector : ITagProjector<TProjector>;
Task<TagState> GetStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector>;
Task<bool> TagExistsAsync(ITag tag);
Task<string> GetTagLatestSortableUniqueIdAsync(ITag tag);   // "" when the tag has no events
Task<EventOrNone> AppendEvent(IEventPayload ev, params ITag[] tags);
Task<EventOrNone> AppendEvent(EventPayloadWithTags eventPayloadWithTags);
```

`GetStateAsync` returns an empty state for a tag with no events.

## Core runtime contracts

| Contract (namespace) | 11 shape |
|---|---|
| `IActorObjectAccessor` (`Sekiban.Dcb.Actors`) | `Task<T> GetActorAsync<T>(string actorId) where T : class;` a failure faults the task |
| `ITagConsistentActorCommon` (`Sekiban.Dcb.Actors`) | `Task<string> GetLatestSortableUniqueIdAsync();` `Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId);` a conflict throws the same exception and message |
| `ISerializedSekibanDcbExecutor` | `Task<SerializableTagState> GetSerializableTagStateAsync(TagStateId tagStateId);` `Task<SerializedCommitResult> CommitSerializableEventsAsync(SerializedCommitRequest request, CancellationToken cancellationToken = default);` |
| `ISerializedExpectedTagPositionSekibanDcbExecutor` | `Task<SerializedCommitResult> CommitSerializableEventsWithExpectedTagPositionsAsync(...)` |
| `ISerializedConditionalSekibanDcbExecutor` | `Task<SerializedConditionalCommitResult> CommitSerializableEventConditionallyAsync(...)` |
| `ISerializedCommitAcceptor` (`Sekiban.Dcb.Commands`) | `Task<SerializedCommitResult> AcceptAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default);` |
| `IProjectionStatusReader` | `Task<IReadOnlyList<ProjectionStatusSnapshot>> ReadAsync(...)` and `GetSnapshotsAsync(...)` |
| `ISerializedProjectionStatusReader` | `Task<byte[]>` for `AcceptAsync`, `ReadSerializedAsync`, `ReadSerializedV2Async`, `GetSerializedSnapshotsAsync` |
| `IMultiProjectionProjectionAccumulator` (`Sekiban.Dcb.Runtime`) | `SerializableMultiProjectionStateEnvelope GetSnapshot();` `MultiProjectionStateMetadata GetMetadata();` |

`CoreGeneralSekibanExecutor.ExecuteAsync` takes a `Func<TCommand, ICoreCommandContext, Task<EventOrNone>>` handler
(was `Task<ResultBox<EventOrNone>>`); its query, tag-state and head-status members return values.
`ProjectionStatusReadWindowCache` takes a `Func<Task<long>>` sampler. Reservation failures still aggregate into
"Failed to reserve tags".

An endpoint that served the serialized status reader now writes the bytes directly:

```csharp
app.MapGet("/ops/projection-status", async (ISerializedProjectionStatusReader reader) =>
        Results.Bytes(await reader.ReadSerializedAsync(), "application/json"))
   .RequireAuthorization("ProjectionStatusOperator");
```

## Queries

- **A query that implements only `IQueryCommon` and declares a static `HandleQuery` returning a `ResultBox` is no longer
  supported.** Implement the WithResult `IMultiProjectionQuery` / `IMultiProjectionListQuery` (box-returning handlers)
  or the core `ICoreMultiProjectionQuery` / `ICoreMultiProjectionListQuery` (value-returning handlers). Such a query
  fails every time it runs with an `InvalidOperationException` whose message names the type and ends with
  "A query whose handlers return a ResultBox must implement the WithResult IMultiProjectionQuery interface; otherwise
  implement ICoreMultiProjectionQuery and return the value directly." (the list form names the list interfaces). There
  is no `TypeInitializationException`, and other query types are unaffected.
- On the name-bound path used by WithoutResult queries, Core no longer opens a `ResultBox` that a handler returns at
  runtime as `object`; it is the result value. This only affects a WithoutResult query whose output type is `object`
  and which returns a box.
- **List queries with value-type outputs** (for example `int`) now work through the core contract; in 10.x they failed
  at `HandleSort`.
- Queries run through the core contract with cached delegates and without exception wrapping.

## Orleans

### Full cluster restart

Grain interfaces changed shape, and `ITagConsistentGrain` gained a member. **10.x and 11 silos and clients cannot run
in one cluster: stop the whole 10.x cluster, then start 11.** Persisted grain state (`MultiProjectionGrainState`,
`TagStateCacheState`), snapshot envelopes and events did not change; the removed `ResultBoxSurrogate` was used only on
RPC calls and never persisted.

### Grain interfaces (Sekiban.Dcb.Orleans.Grains)

```csharp
// IMultiProjectionGrain
// 10.x
Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false);
Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true);
Task<ResultBox<bool>> PersistStateAsync();
Task<ResultBox<ProjectionFaultReadResult>> TryGetProjectionFaultAsync();
Task<ResultBox<bool>> ResetProjectionFaultAsync(ResetProjectionFaultRequest request);
// 11
Task<MultiProjectionState> GetStateAsync(bool canGetUnsafeState = true, bool waitForCatchUp = false);
Task<string> GetSnapshotJsonAsync(bool canGetUnsafeState = true);
Task<bool> PersistStateAsync();
Task<ProjectionFaultReadResult> TryGetProjectionFaultAsync();
Task<bool> ResetProjectionFaultAsync(ResetProjectionFaultRequest request);

// ITagConsistentGrain
// 10.x
Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();
Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId);
// 11
Task<string> GetLatestSortableUniqueIdAsync();
Task<string?> TryGetLatestSortableUniqueIdAsync();   // new
Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId);
```

- `PersistStateAsync` returns `false` when the attempt stopped without a durable result (an integrity guard, a pending
  rebuild); a failed attempt faults the task.
- The default body of `TryGetProjectionFaultAsync` returns a task faulted with `NotSupportedException`.
- `ResetProjectionFaultAsync` faults with the same `InvalidOperationException` on a token mismatch.
- `TryGetLatestSortableUniqueIdAsync` returns `null` when the actor reports a failure (latest position unknown); only a
  failed grain call faults the task. The tag-state reader uses it to keep the 10.x distinction between the actor's own
  failure and a transport or timeout failure.
- A grain-call failure and a grain-reported failure both fault the task on the public members. Code that called the
  grains directly and inspected `IsSuccess` must catch instead.

### Custom `IProjectionActorHost` (Sekiban.Dcb.Runtime)

```csharp
// 11 (10.x returned Task<ResultBox<...>> for each of these)
Task<ProjectionStateMetadata> GetStateMetadataAsync(bool includeUnsafe = true);
Task<MultiProjectionState> GetStateAsync(bool canGetUnsafeState = true);
Task<bool> WriteSnapshotToStreamAsync(Stream target, bool canGetUnsafeState, CancellationToken cancellationToken);
Task<bool> WriteSnapshotForPersistenceToStreamAsync(Stream target, bool canGetUnsafeState,
    int offloadThresholdBytes, CancellationToken cancellationToken);
Task<bool> RestoreSnapshotFromStreamAsync(Stream source, CancellationToken cancellationToken);
Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter query, int? safeVersion,
    string? safeThreshold, DateTime? safeThresholdTime, int? unsafeVersion);
Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter query, int? safeVersion,
    string? safeThreshold, DateTime? safeThresholdTime, int? unsafeVersion);
Task<bool> RewriteSnapshotVersionAsync(Stream source, Stream target, string newVersion,
    CancellationToken cancellationToken);
```

The snapshot write members return `false` when the host wrote no durable snapshot; a failed write or restore throws. A
host whose snapshot write or restore throws is handled like a host-reported failure (a warning and a failed persist
attempt). `NativeProjectionActorHost` is the built-in implementation.

### Serialization helpers (Sekiban.Dcb.Orleans.Serialization)

`SerializableQueryParameter.ToQueryAsync` returns `Task<object>`. `SerializableQueryResult.CreateFromResultBoxAsync`
takes `object result`, `SerializableListQueryResult.CreateFromResultBoxAsync` takes `IListQueryResult listQueryResult`,
`ToQueryResultAsync` returns `Task<QueryResultGeneral>`, `ToListQueryResultAsync` returns
`Task<ListQueryResultGeneral>`, and `SerializableQueryResult.ToResultBoxAsync` returns `Task<object>` (the value). The
names are kept although they no longer use `ResultBox`. Failures that 10.x returned unwrapped still throw unwrapped;
others keep the "Failed to deserialize …" `InvalidOperationException`. `OrleansActorObjectAccessor` follows
`IActorObjectAccessor`.

### Exception transport

Orleans 10.4.2 serializes System, Microsoft and Azure exception types natively. Any other exception type without a
codec reaches the caller as `CodecNotFoundException`, and its message is lost. 11 handles this as follows:

- **Converters for Sekiban exceptions** in Orleans.Core, registered with `[RegisterConverter]` (no configuration needed):
  `ExpectedTagPositionConflictException`, `ConditionalAppendInDoubtException`, `KeyReuseConflictException`,
  `TagHeadExpectationValidationException` (all `Sekiban.Dcb.Storage`) and, as before, `SekibanProjectionFaultException`
  (`Sekiban.Dcb.Actors`, with its descriptor). They are rebuilt through their constructors, so type, message and
  properties arrive; stack traces and other `Data` entries do not travel (a `SekibanProjectionFaultException` re-applies
  its fault annotations on arrival).
- **On the members that used to return boxes** (the five `IMultiProjectionGrain` members and the two
  `ITagConsistentGrain` members above), a failure travels as the same exception instance when Orleans can serialize it.
  Before it is sent, only `InnerException`, `AggregateException.InnerExceptions` and `Data` are walked. Reaching the
  same exception instance a second time (for example an `AggregateException` that lists one inner exception twice),
  going deeper than 64 levels or visiting more than 1024 exceptions makes the check fail, and the failure falls back
  as described below. Every `Data` key and value must be `null`, a string, a
  primitive, an enum, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan` or `Guid`. The five converter types above
  are the exception to this rule: their surrogate carries only its own fields, so their `Data` is not inspected (a
  `SekibanProjectionFaultException` with a self-referencing `Data` entry still travels through its converter), and their
  inner exception is checked only for `ConditionalAppendInDoubtException` and `KeyReuseConflictException`, whose
  surrogates carry it. Only after this walk is the exception serialized with the silo serializer as a test.
- **Fallback.** A failure that does not pass becomes `Exception("[TypeName] message")`, the form 10.x used on these
  members for every failure except `SekibanProjectionFaultException`. The check is conservative: an exception without a Sekiban converter whose `Data` (or an inner
  exception's `Data`) holds a complex value is flattened even if Orleans could carry it.
- **Cancellation is preserved.** A cancellation that cannot travel becomes `OperationCanceledException` (or
  `TaskCanceledException`) with the same message, without inner exception or `Data`.

So Sekiban types with a converter keep type, message and properties, System types keep type and message, and types
without a codec keep the `[TypeName] message` form. **The `[TypeName]` prefix is gone wherever a codec exists.** A
caller of these grain members gets the faulted task whichever facade it uses. Neither `OrleansDcbExecutor` exposes the
`IMultiProjectionGrain` members; on the executor path, `ICommandContext.TagExistsAsync` and
`GetTagLatestSortableUniqueIdAsync` reach `ITagConsistentGrain.GetLatestSortableUniqueIdAsync` (the transported
exception arrives in the WithResult failed box, or is thrown by WithoutResult), and reservations reach
`MakeReservationAsync`, whose failures are folded into `InvalidOperationException("Failed to reserve tags: ...")`.

## MaterializedView

`MvExecutorBase<T>` replaced its protected `ResultBox` overloads:

```csharp
// 10.x (removed)
protected Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    ResultBox<IEnumerable<SerializableEvent>> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    ResultBox<IEnumerable<SerializableEvent>> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures)

// 11: the events you read
protected Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    IEnumerable<SerializableEvent> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    IEnumerable<SerializableEvent> readResult, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures)

// 11: why the read failed (new)
protected async Task<MvCatchUpResult> CompleteCatchUpAsync(IMvApplyHost host, string serviceId,
    Exception readFailure, MvProjectionStatusSnapshot currentStatus,
    CancellationToken cancellationToken, bool classifyFailures = true)
```

Pass the events on success; on a failed read pass the exception, which is classified exactly like a failed read inside
`CatchUpOnceAsync` (through `HandleCatchUpReadFailure`).

## Behaviour changes beyond shape

Everything not listed here keeps its 10.x behaviour, apart from the shape change.

### Storage and providers

1. Not-found reads are `null` values instead of errors. An existing but unreadable SQLite row still throws
   `KeyNotFoundException`, as in 10.x.
2. Cold paths keep their 10.x outcome unless the caller's own token is cancelled: `HybridEventStore` list reads fall back
   to hot on any cold failure; streaming reads fall back to hot unless the caller's token is cancelled;
   `ColdControlFileHelper` treats an unreadable manifest or checkpoint as absent unless the caller's token is
   cancelled; `ColdExportCycleRunner` logs and ends the cycle unless the cycle token is cancelled.
3. Runtime and Orleans call sites no longer distinguish a thrown storage failure from a returned one. Exceptions thrown
   by the grain's own streaming callback still propagate; only store failures count as stream read failures. For Cosmos
   DB, failures that 10.x let escape (for example a 429 on the pre-save snapshot read) now take the skip path that
   PostgreSQL, DynamoDB and SQLite already took: grain state is saved, the external snapshot is retried on the next
   persist, and `_lastError` is set. No data is overwritten or lost.
4. MaterializedView target-checkpoint capture maps any failed head read, provider timeouts included, to
   `ReadUnavailable`, as in 10.x.
5. `MergeCapacityExceededException` now reaches its intended catch in the cold exporter tail merge (same plan, one
   warning log line fewer).
6. Logging: DynamoDB adds no error logs for failures 10.x returned as boxes; SQLite cache sync logs "Cache sync failed"
   for store failures during sync, now also for remote failures 10.x returned as boxes.

### Core.Model

7. Unknown names in registries throw (same exception type and message as the former failed box).
8. A domain registry assembly compiled against an old Sekiban (the repository's 10.18 fixture) no longer loads
   (`TypeLoadException`). Rebuild against 11.

### Core runtime

9. `ColdExporter`: a failed re-read of a staged local segment propagates the original exception (10.x:
    `ResultsInvalidOperationException("no value")` from `GetValue()`).
10. A WithResult command handler that returns a null `ResultBox` fails with `InvalidOperationException` (was
    `NullReferenceException`). Query handlers keep the 10.x messages ("HandleQuery returned null result", "Query handler
    returned invalid result type").
11. Synchronous throws keep their 10.x path: only faulted tasks are boxed where 10.x boxed them (WithResult facade,
    `ColdExporter`, `DurableSubscriptionRunner`, command-context and tag-state helpers).
12. Queries: see [Queries](#queries) (unsupported `IQueryCommon`-only box handlers, the name-bound `object` case, and
    value-type list outputs).
13. The WithoutResult and Orleans.WithoutResult serialized members return values (see
    [WithoutResult users](#withoutresult-users)).
14. `GeneralTagStateActor` (in-memory path only): a transport failure while reading the latest position becomes
    "unknown latest", as a failed box always did.
15. Orleans `TagStateGrain` reads the latest position through the Try member: an actor-reported failure means "unknown
    latest", while a failed grain call (transport, timeout) propagates, as in 10.x.

### Orleans

16. Formerly boxed grain members keep the real exception type and message; the `[TypeName]` prefix is gone where a codec
    exists.
17. `ITagConsistentGrain.TryGetLatestSortableUniqueIdAsync` was added.
18. Grain-call failures and grain-reported failures both fault the task on the public members.
19. `NativeProjectionQueryExecutor`: a custom `IQueryTypes` that returns `null` fails with
    `InvalidOperationException("Query X completed without a result.")` instead of the ResultBoxes "no exception" error.
20. A third-party `IProjectionActorHost` whose snapshot write or restore throws is handled like a host-reported failure
    (a warning and a failed attempt) instead of the outer "Persistence failed" log.
21. Persist and snapshot endpoints in the samples and templates: a grain-call failure (for example an Orleans timeout)
    now takes the same warning and BadRequest path as a grain-reported failure. 10.x let transport failures reach the
    exception middleware. The two arrive as the same faulted task and cannot be told apart reliably by exception type.
22. ResultBox null-value quirks (a `null` value treated as a failure, for example a null stream or a null payload from
    deserialization) are no longer reproduced; returning `null` there is a contract violation.

### WithoutResult

23. `RegisterProjectorWithCustomSerialization` throws the original exception on failure (10.x returned a failed box,
    usually ignored).
24. Assembly-scan registrations other than custom-serialized projectors (`RegisterProjector`, events, tag projectors,
    tag-state payloads, tag types, queries, list queries) surface the original exception instead of
    `TargetInvocationException`, in both facades. A scanned custom-serialized projector whose registration fails is
    still skipped silently and the scan continues, as in 10.x.
25. Generic tag projectors' public static `Project` with a `null` payload and no tag of the projector's tag group throws
    `InvalidOperationException("{Projector}.Project produced no payload: the payload passed in was null and the event
    carried no tag of the projector's tag group.")` with the same boundary annotations (10.x: ResultBoxes'
    `ResultValueNullException`).
26. Generic tag projectors' snapshot restore skips any item whose payload deserialization throws. In 10.x only
    `SimpleTagStatePayloadTypes` led to a skip; `AotTagStatePayloadTypes` and user `ITagStatePayloadTypes` let the
    failure propagate.
27. The box-only null-result guard case is gone; a null `Task` or null value still gives `SekibanBoundaryException`.
28. Applications that used ResultBox types only through WithoutResult's transitive reference must reference
    `ResultBoxes` themselves.

### Repository internals

The internal sample's `/debug/events` endpoint returns 500 instead of 400 on a store failure, and the test helper
`SeamWriteScanner` decodes IL at instruction boundaries. Neither ships in a package.

## API break record

Every intended break against 10.25.0 is recorded, per member and target framework, in the `CompatibilitySuppressions.xml`
file of each package under `dcb/src/`. Package validation fails the pack on any break that is not listed there.
Packages with entries: Core, Core.Model, Core.Testing, Postgres, CosmosDb, DynamoDB, Sqlite, ColdStorage,
BlobStorage.AzureStorage, BlobStorage.S3, MaterializedView, Orleans.Core, Orleans.WithoutResult, WithoutResult,
WithoutResult.Model, WithResult and WithResult.Model. Packages without entries: Orleans.WithResult, WithResult.Testing,
WithoutResult.Testing, Orleans.AzureQueue, MaterializedView.Orleans and the four MaterializedView database providers.
