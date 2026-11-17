# Sekiban DCB 11.0.1-preview01

- Version: `11.0.1-preview01`. The first preview of the DCB 11 line. 26 DCB library packages.
- Targets **net10.0 and net11.0** (net9.0 is not targeted; projects that still target net9.0 must stay on 10.24.x). Built with C# 15 and the .NET 11 SDK `11.0.100`. Consuming the packages does not require the .NET 11 SDK on net10.0.
- **ResultBox is now only on the WithResult surface.** This release breaks the public API of 10.25.0: recompile against 11.0.1-preview01 and read the migration guide, `docs/dcb_llm/24_migrating_to_11.md` (Japanese: `docs/dcb_llm_ja/24_migrating_to_11.md`).
- No DCB event data migration is required. Events, tags, snapshots and persisted Orleans grain state are unchanged.

## ResultBox only on the WithResult surface

Only `Sekiban.Dcb.WithResult`, `Sekiban.Dcb.WithResult.Model`, `Sekiban.Dcb.Orleans.WithResult` and `Sekiban.Dcb.WithResult.Testing` depend on `ResultBoxes`. Core, Core.Model, Core.Testing, every storage provider, Orleans.Core, MaterializedView and the WithoutResult packages are exception based. The conversion follows four rules:

1. Failures are exceptions of the same type and message as before.
2. Expected outcomes are values (`null`, `false`, an empty string or an empty state), for example a missing event or snapshot.
3. Cancellation stays `OperationCanceledException`.
4. Contracts change shape only: `ResultBox<T>` becomes `T`; names and parameters are kept.

Every intended API break against 10.25.0 is recorded in the `CompatibilitySuppressions.xml` file of each package under `dcb/src/`.

## Who is affected

- **WithResult users.** The API you write against is unchanged: command handlers, projectors, queries, `ICommandContext` and `ISekibanExecutor` still return `ResultBox`. On Orleans, a failed box may now carry the original exception type rather than a flattened `Exception("[TypeName] message")`, for example when `ICommandContext.TagExistsAsync` or `GetTagLatestSortableUniqueIdAsync` fails inside the tag grain (such as `InvalidOperationException("Actor not initialized")`). Call failures (timeouts, transport errors) and event-store failures behave as in 10.x on this path. Reservation failures are still folded into "Failed to reserve tags: ...", whose per-tag text may lose the prefix. A few source changes can still reach your code: `SortableUniqueId.Parse` returns the value and throws `ArgumentException`, `ToTypedResult<T>()` returns the value, `ConsistencyTag.SortableUniqueId` is nullable, `ICommandContext` no longer derives from `ICoreCommandContext`, and code that calls the core contracts directly gets values.
- **WithoutResult users.** The serialized members of `GeneralSekibanExecutor` (`GetSerializableTagStateAsync`, `CommitSerializableEventsAsync`, `CommitSerializableEventsWithExpectedTagPositionsAsync`, `CommitSerializableEventConditionallyAsync`) and the first three on `OrleansDcbExecutor` return `Task<T>` instead of `Task<ResultBox<T>>`. `SimpleMultiProjectorTypes.RegisterProjectorWithCustomSerialization<T>()` returns `bool` and throws the original exception on failure. WithoutResult no longer brings `ResultBoxes` transitively: reference it yourself if you use it. Failures keep their type and the `Sekiban.Boundary.Operation` / `Sekiban.Boundary.Target` data keys.
- **Custom storage providers and `IEventStore` implementers.** Every storage contract returns values and throws: `IEventStore`, `IMultiProjectionStateStore`, `IProjectionStatusStore`, `IGenerationAwareCheckpointStore`, `IConditionalEventStore`, `IExpectedTagPositionEventStore`, the streaming stores, the cold storage contracts and `IDurableSubscriptionStore`. Not-found reads return `null` (`ReadSerializableEventAsync`, `GetLatestForVersionAsync`, `GetLatestAnyVersionAsync`, `IColdObjectStorage.GetAsync`), deletes of a missing item return `false`, and `ReadCheckpointSlotAsync` returns `CheckpointSlot.Absent`.
- **Custom `I*Types` implementers.** `ICoreMultiProjectorTypes`, `IStreamingMultiProjectorTypes`, `ITagProjectorTypes`, `ITagStatePayloadTypes`, `ICoreQueryTypes` and the static core contracts (`ICoreMultiProjector<T>.Project`, `ICoreMultiProjectionQuery.HandleQuery`, `ICoreMultiProjectionListQuery.HandleFilter` / `HandleSort`, `ICoreCommandContext`) return values. An unknown name throws.
- **Orleans clusters.** A full cluster restart is required: grain interfaces changed (`IMultiProjectionGrain`, and `ITagConsistentGrain`, which gains `TryGetLatestSortableUniqueIdAsync`), so 10.x and 11 silos and clients cannot run together. Persisted grain state, snapshots and events did not change. A custom `IProjectionActorHost` returns values.
- **Exception transport.** Sekiban's conflict, in-doubt, key-reuse and tag-head validation exceptions now have Orleans converters, and the formerly boxed grain members send a failure as itself when Orleans can serialize it; callers of those members get a faulted task. The `[TypeName]` prefix is gone wherever a codec exists (10.x already kept `SekibanProjectionFaultException` intact). Types without a codec, and exceptions without a Sekiban converter whose `Data` holds non-simple values, still arrive as `Exception("[TypeName] message")`; the five Sekiban converter types travel through their converters regardless of `Data`. Cancellation stays `OperationCanceledException`.
- **Queries.** A query that implements only `IQueryCommon` with a static `HandleQuery` returning `ResultBox` is no longer supported and fails with an `InvalidOperationException` that names the interface to implement. List queries with value-type outputs now work through the core contract.
- **MaterializedView.** The protected `MvExecutorBase.CompleteCatchUpAsync` overloads take `IEnumerable<SerializableEvent>`, and a new overload takes the `Exception` of a failed read.

The migration guide lists each changed signature and every behaviour change beyond shape.

## Dependencies

- **net10.0** uses the same dependency versions as DCB 10.25.0.
- **net11.0** uses the .NET 11.0.0 GA packages: `Microsoft.Extensions.*` (those versioned with .NET) and `Microsoft.Data.Sqlite` `11.0.0`. The PostgreSQL provider is the exception: `Npgsql.EntityFrameworkCore.PostgreSQL` `11.0.0-rc.3` with Entity Framework Core `11.0.0-rc.3.26522.148`, because Npgsql pins that exact EF Core version. `Npgsql` is 10.0.3 on net11.0.
- `Microsoft.Orleans.*` is 10.4.2 on both. For the Orleans 10.4.2 upgrade notes, see the 10.24.0 release notes.
- `ResultBoxes` 0.4.0 is a dependency of the four WithResult packages only.

## Preview status

- On net11.0, `Npgsql.EntityFrameworkCore.PostgreSQL` and the Entity Framework Core it pins are release candidates. A later preview moves both to GA. The stable 11.0.1 follows when all dependencies are stable.
- The 10.x line continues to receive releases while the 11 line is in preview.

## Templates

On the 11 line, the five DCB templates reference `Sekiban.Dcb` `11.0.1-preview01` (`SekibanDcbVersion` in each template's `SekibanDcbTemplateVersion.props`) and `Microsoft.Orleans` 10.4.2; generated projects target net10.0. Their sources were changed only where the 11 API required it. Templates are not released together with these library packages: adjusting them to the 11 best practice and releasing them is a separate, later step.
