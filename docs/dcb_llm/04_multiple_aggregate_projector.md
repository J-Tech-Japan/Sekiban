# MultiProjection - Composable Read Models

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md) (You are here)
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
> - [Materialized View Basics](20_materialized_view.md)

While tag projectors keep per-tag state, MultiProjection composes those states into application-specific read models.
Each MultiProjection runs in its own Orleans grain (or actor) and can offload large snapshots to Azure Blob Storage.

## Anatomy of a MultiProjection

Implement `IMultiProjector<T>` and describe how tag events roll up into projection state.

```csharp
public class WeatherForecastProjection : IMultiProjector<WeatherForecastProjection>
{
    public static string MultiProjectorName => "WeatherForecast";
    public static string MultiProjectorVersion => "1.0.0";

    public static MultiProjectionState Project(
        MultiProjectionState current,
        Event currentEvent,
        IReadOnlyDictionary<ITag, TagState> tagStates)
    {
        // Access tag states that were touched by the event
        // Combine them into a projection payload
    }
}
// Source: internalUsages/Dcb.Domain/Projections/WeatherForecastProjection.cs
```

Helpers like `GenericTagMultiProjector<TProjector, TTag>` let you generate list-style projections without bespoke code.
The sample domain registers multiple generic projectors in `internalUsages/Dcb.Domain/DomainType.cs`.

## State Lifecycle

1. Tag events arrive through Orleans streams or polling.
2. The MultiProjection grain requests latest tag states from `TagStateGrain`.
3. Projection state is updated in memory and optionally offloaded via `IBlobStorageSnapshotAccessor`.
4. Queries read from the MultiProjection grain, which can enforce `WaitForSortableUniqueId` semantics for fresh data.

`src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrain.cs` contains the orchestrator that wires these pieces together.

## Passive projection status (SEK-G24 / dcb-v10.10.0)

Fleet dashboards can read a catch-up sample without activating a projection grain. Provider registrations include the
passive `IProjectionStatusReader` and `ISerializedProjectionStatusReader` surfaces:

```csharp
var reader = serviceProvider.GetRequiredService<IProjectionStatusReader>();
var result = await reader.ReadAsync(new ProjectionStatusReadRequest(ProjectorName: "WeatherForecast"));
```

Each `MultiProjectionGrain` writes a best-effort heartbeat on a dedicated 30-second timer. The timer is interleaved,
non-keep-alive, and uses one CAS row per `(ServiceId, ProjectorName, ProjectorVersion, ClusterId)`; `ActivationId` is
data in that row, so a replacement activation cannot create a second row or bypass the sequence fence. Storage writes
use an independent bounded timeout, retry with capped backoff, and rate-limit repeated failure logs. Projection
execution is never blocked by a status write. The passive reader samples one event-store denominator per service per
sampling window (five seconds by default), then counts events after each distinct `LastTraversedSortableUniqueId`
with bounded parallelism; this cursor includes filtered events, so `AppliedEventCount` may be smaller than the
traversed head while `RemainingEventCount` is zero. `IsCaughtUp` additionally requires a fresh leased row that is
not faulted and has no fresh-cluster conflict. Every sample carries `SampledAtUtc` and `Consistency == "bestEffort"`;
it is not an atomic head/count transaction.

Use the three status tiers for different operator questions: (1) the passive registry for a fleet-wide catch-up
overview, (2) the persisted snapshot APIs for restoration/checkpoint detail, and (3) an activated grain query when an
authoritative, current projection result is required. The existing snapshot and grain status APIs remain unchanged.

For Cloud/WASM transport, use the additive `ISerializedProjectionStatusReader` and its V1 envelope. The serialized
boundary binds `ServiceId` from the server's `IServiceIdProvider`; a client cannot select another service. Keep the
endpoint operator-only and default-deny it at the host boundary: map it only when needed and require an explicit
authorization policy (for example, `RequireAuthorization("ProjectionStatusOperator")`). Do not expose it with
`AllowAnonymous`; the existing `ISerializedSekibanDcbExecutor` contract is unchanged.

## Snapshot Offloading

Large projections can use `Sekiban.Dcb.BlobStorage.AzureStorage` to persist snapshots in Azure Blob Storage.
Register an accessor:

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

The Orleans grain detects the accessor and periodically checkpoints state, reducing silo memory usage
(`src/Sekiban.Dcb.Orleans/Grains/MultiProjectionGrainState.cs`).

### Streaming restore for offloaded snapshots

When an offloaded snapshot is restored, Sekiban opens the blob payload once and carries that non-seekable stream through
the resolver and actor to the projector registry. The built-in reflection and AOT JSON registries use this path. A custom
projector can opt in per projector by implementing the additive `ICoreMultiProjectorWithStreamDeserialization`; registries
expose the separate `IStreamingMultiProjectorTypes` capability. `ICoreMultiProjectorTypes` itself is unchanged, so
existing external registries remain supported.

The guarantee is deliberately limited: for an **offloaded** snapshot whose projector supports the capability, restore
does not materialize an additional contiguous `byte[]` or `string` proportional to the complete uncompressed payload.
The projection graph still has to exist (and can dominate memory), so this is **not a no-OOM guarantee**. Sekiban uses a
temporary file only to create the independent safe/unsafe restore graphs; it does not create a whole managed payload
buffer. Save-side streaming and compression-format changes are outside this restore guarantee.

| Snapshot and registry condition | Restore behavior | Guaranteed non-buffering path? |
| --- | --- | --- |
| Offloaded payload + capability present | The opened stream is passed to the projector, using async reads and the current stream position. Reflection/AOT JSON accepts gzip or raw legacy JSON. | Yes |
| Offloaded payload + custom projector implements the stream capability | The custom projector receives the caller-owned stream. | Yes, subject to the custom implementation honoring the contract |
| Offloaded payload + capability absent | One observable compatibility fallback buffers the payload and logs projector, registry, `Format=offloaded`, and `Reason=capability-absent`; payload content is never logged. | No |
| Offloaded payload + capability present but open/read/decompress/deserialize fails | The original failure is returned. Sekiban makes **zero** buffered retries. | No successful restore; fail closed |
| Inline JSON/Base64 (including legacy v9/V10 inline envelopes) | The existing inline restore remains buffered for compatibility. | No — inline Base64 is explicitly outside this guarantee |

Stream implementations must use asynchronous reads, honor `CancellationToken`, support non-seekable partial-read streams
at their current position, and never dispose the stream. The resolver caller owns disposal. While a stream restore is in
progress, state queries, event application, promotion, compaction, and snapshot persistence fail rather than publishing
old or partial payload/tracking metadata. When a terminal restore failure leaves an already-published payload or
tracking metadata, that same fail-closed barrier remains latched: the previous checkpoint is not usable by query,
apply, catch-up, promotion, or persistence. A failed first restore has no prior payload to serve and retains the legacy
empty-state/rebuild path. A later restore/rebuild attempt is still permitted, and only a successful atomic restore
clears a latched barrier; otherwise the host follows its normal recovery/catch-up policy without serving stale state.

#### Restore caller inventory

| Caller | Snapshot shape | Path |
| --- | --- | --- |
| `MultiProjectionGrain` → `NativeProjectionActorHost` → `NativeProjectionSnapshotHandler` | Orleans state-store activation; this is the production incident/OOM entry point | Opens the outer state stream, calls `SnapshotEnvelopeResolver.ResolveForRestoreAsync`, then awaits `GeneralMultiProjectionActor.SetResolvedSnapshotAsync` |
| `MultiProjectionStateBuilder.LoadRestoreAsync` | Offline/builder checkpoint restore | Deserializes the outer envelope, resolves the offloaded payload stream, and awaits the same actor seam |
| `NativeMultiProjectionProjectionPrimitive.ApplySnapshot` | Inline primitive snapshot | Calls `SetSnapshotAsync`; inline compatibility path only |
| `GeneralMultiProjectionActor.SetCurrentState` / `SetCurrentStateIgnoringVersion` | Direct legacy inline state | Buffered compatibility path only |
| `SnapshotEnvelopeResolver.ResolveInlineAsync` | Explicit compatibility adapter | Materializes only because its caller explicitly asks for an inline envelope; production offloaded restore must use `ResolveForRestoreAsync` |

The normal DCB test suite includes a controlled small-graph, 16–32 MiB offloaded gzip wire fixture. It combines a
production aggregation counter with structural guards that reject whole-payload aggregation APIs from the supported
stream seam. The separate **DCB Streaming Restore
Memory Smoke** workflow also runs that controlled fixture in its own process with an allocation ceiling and an
intentionally buffered control which must exceed it. Its 143 MiB fixture runs only on a weekly/manual schedule with a
timeout and virtual-memory ceiling. The workflow records elapsed time, peak RSS, selected capability path, read counts,
and buffer counters; it evaluates absence of the full-payload materialization path, not a claim that an OOM is
impossible.

## Consistency Considerations

- MultiProjection receives events in global order; use `WaitForSortableUniqueId` on queries to avoid stale reads.
- Because tag states are cached, projector code must be deterministic and side-effect free.
- Projection version changes trigger a rebuild. Bump `MultiProjectorVersion` whenever schema or logic changes.

## Practical Use Cases

- Aggregated dashboards (counts, availability, leaderboards)
- Materialized list views for Blazor components
- Cross-tag joins without hitting the primary event store

Refer to `internalUsages/Dcb.Domain/Student/StudentSummaries.cs` for a concise example of projecting multiple tags into a
domain-specific summary list.

## MultiProjection vs. Materialized View

Sekiban now supports two different read-model styles:

- **MultiProjection**: In-memory projection state hosted by Orleans grains. Best when the read model is naturally consumed
  through `ISekibanExecutor.QueryAsync`.
- **Materialized View**: Database tables updated from the same ordered event stream. Best when you need SQL paging,
  filtering, reporting, or direct table access from external tooling.

Use MultiProjection when you want the simplest end-to-end Sekiban query path. Use materialized views when the read model
must live in a relational database. See [Materialized View Basics](20_materialized_view.md).

## Dual-State Convergence & Safe-Window Graduation (SEK-G18)

Multi-projections keep two states: a **safe** state (events older than the safe window,
in global `SortableUniqueId` order) and a **served/unsafe** state (what queries return).

- **Served state is reconciled, not arrival-ordered.** At every safe-window graduation the
  served state is re-derived as `safe baseline + still-buffered events replayed in global
  SortableUniqueId order`, then published atomically. Two events that arrive out of order
  (e.g. a cross-instance duplicate create) therefore converge to the same result on every
  instance — the globally-earliest event wins for a first-event-wins projector, regardless
  of local arrival order.
- **`IsSafeState` is truthful.** It is `true` only when the served state was published
  identical to the safe state (no buffered events remain, no rebuild pending) — never from
  a timestamp comparison alone. A query that returns `IsSafeState=true` is guaranteed to be
  the reconciled, globally-ordered value.
- **Ordering-regression rebuild (fail-closed).** If an event promotes to safe out of global
  order versus the held safe head, the incremental (compacted-baseline) path cannot reorder
  it. The projection then performs a **full ordered rebuild from the authoritative event
  store** from the initial state; while rebuilding, every state/scalar/list query awaits the
  rebuild barrier and answers with the rebuilt payload or fails closed — it never returns a
  stale success. The G14 fault path is reserved for failures OF the rebuild itself.

### Checkpoint-restore exactness (SEK-G18 / #1086)

- **Catch-up start is authoritative.** After a checkpoint restore, catch-up starts from the
  checkpoint record's `LastSortableUniqueId`, read **exclusive** of that position — an event
  whose id equals the checkpoint position is already reflected in the restored payload and is
  not re-read, so it is never double-counted or re-folded. (All event stores —
  Postgres/SQLite/Cosmos/DynamoDB, the in-memory store, and the Hybrid cold→hot handoff — use
  a strict `SortableUniqueId > since` filter.)
- **`EventsProcessed` is a durable safe-checkpoint count** used as an integrity signal:
  restore takes it as the baseline; a restart that writes zero new events restores exactly the
  same payload/position/threshold/count.

### Catch-up persist cadence and telemetry (SEK-G37 / #1142)

Catch-up completion is defined by `FetchedCount == 0`. A non-empty read whose events are
all filtered (`AppliedCount == 0`) still advances the traversal cursor and reaches the
same progress, persist-decision, and telemetry seam as an applied batch. This prevents a
filtered tail from ending catch-up before its checkpoint fallback can run.

The existing `event_count_checkpoint` trigger remains first on the hot-only path. The
additive fallbacks report `PersistReason=fetched_count_checkpoint` after 5,000 fetched
events or `PersistReason=time_checkpoint` after five minutes. Cold reads retain their
configured segment, applied-count, and interval triggers, plus the fetched-count fallback;
the cold/hot choice is taken from the read metadata's `UsedCold` value. A hybrid store
with `UsedCold=false` therefore uses the hot-only constants. The summary reports
`PersistTriggered` (the decision) separately from `PersistOutcome` (`durable_write`,
`no_durable_write`, or `not_attempted`); a trigger is not evidence that a durable
checkpoint was committed.

### First-query catch-up position contract (SEK-G21 / 10.8.1)

A fresh Orleans activation places a fail-closed barrier in front of its first state, snapshot,
scalar, or list query. The barrier uses two deliberately different positions:

- **START** is the safe/restored checkpoint. A restored record's `LastSortableUniqueId` is leased
  once by a single internal resolver shared by background and in-call catch-up. This deliberately
  re-reads the complete uncheckpointed tail, including an in-window poison event.
- **REACHED** is the authoritative cursor returned by that specific in-call event-store read. It is
  not the safe position and is not read from shared timer progress. This lets a cold first query
  return the current unsafe state immediately after its own read reaches the fixed head, without
  waiting for safe-window graduation.

A short read that does not reach the fixed head still fails closed and remains retryable. A failed
read preserves the original exception. The safe checkpoint, SafeWindow behavior, public API, and
storage schema are unchanged.
