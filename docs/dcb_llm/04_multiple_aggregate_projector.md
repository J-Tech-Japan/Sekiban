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

### Enumerating referenced offload keys

Snapshot blobs can be referenced in two places: the row's `IsOffloaded` / `OffloadKey` / `OffloadProvider` fields, and the `OffloadedState.OffloadKey` / `StorageProvider` fields inside a `SerializableMultiProjectionStateEnvelope`. If the row itself is offloaded, its blob contains the envelope, so both references matter. Current writers store envelopes as plain JSON; gzip support is read-side tolerance only. Orleans `MultiProjectionGrainState` stores no offload key; its dead legacy `SerializedState` field is only cleared. The store row remains the reference source.

Use the additive Core helper `Sekiban.Dcb.Snapshots.OffloadKeyEnumerator`:

```csharp
await foreach (var reference in OffloadKeyEnumerator.EnumerateAsync(
    store, domainTypes.JsonSerializerOptions, cancellationToken))
{
    // Kind: Row, Envelope, or Undecodable.
    // Lifecycle: Active / Tombstoned when available; otherwise null.
    // Collect keys as a union; any Undecodable must stop deletion.
}
```

The helper scans every listed projector version, including tombstoned rows, and resolves row blobs before inspecting envelopes. Pass the application's `JsonSerializerOptions` when using a non-default naming policy. It reads forward-only without deserializing `InlineState`; the buffer can grow to fit the largest single JSON token, so memory is not constant. A lookup, open, or decode failure produces `Undecodable` with `Detail`; list failures and caller cancellation propagate. A missing or unreadable checkpoint slot leaves `Lifecycle` null with `Detail`, while preserving keys.

Enumeration is **observational**: absence of a key does not by itself authorize deletion. Some stores list with eventually consistent reads (for example Dynamo `ListAllAsync`), so even a successful scan may miss a row. Run the helper once per ServiceId sharing a blob container, because each store scan is scoped to its current ServiceId and blob keys carry no ServiceId.

A safe external GC must follow all of these rules:

- Never delete a key referenced by any row of any version or service, including tombstoned rows. Treat the output as a union: version rewrites copy keys and content addressing de-duplicates blobs, so several rows can reference the same key.
- Any `Undecodable` means **delete nothing**. A row deleted between list and lookup appears as `Undecodable`; a re-run usually clears that observation.
- Obtain a complete, consistent reference view, and apply a grace period. Blobs are uploaded before their row commits, and a failed CAS can leave an orphan.
- Re-check references at deletion time and use coordination or a conditional delete that protects concurrent uploads and commits. Content-addressed keys can be re-uploaded by a writer between a GC's check and delete; re-checking alone does not close that race.

The deletion API and coordination protocol are #1253 item 3 and are **not provided here**.

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

`Project` must treat its input payload as immutable and return a new payload instance. If an existing projector
deliberately mutates and returns its input, its payload must implement `IMutatesProjectionInput`, and
`GenerateInitialPayload` must return a fresh instance on every call. The marker makes the dual-state wrapper isolate the
safe baseline through the snapshot serializer. Every reconcile, including the reconcile after each safe in-order event
during catch-up or rebuild, creates an independent clone of safe and therefore costs O(state size). In-order unsafe
folds apply to the already-independent served instance without another clone. This opt-in trades throughput for safety;
prefer a non-mutating (immutable or copy-on-write) projector for large states. The public
`DualStateProjectionWrapper<T>` constructor whose first parameter is `DcbDomainTypes` and
`DualStateProjectionWrapperFactory.CreateWithDomainTypes` clone the initial marker payload once through the registered
snapshot serializer. Their legacy
overloads without `DcbDomainTypes` fail fast for marker payloads because they cannot guarantee serializer-correct
isolation; they retain their existing `System.Text.Json` behavior for non-marker payloads. Operators can temporarily enable
`GeneralMultiProjectionActorOptions.VerifySafeStateIsolation` to fail fast when an unmarked projector mutates safe state;
the diagnostic is off by default because it serializes the safe payload around unsafe folds and compares the serialized
bytes. Serialization must be deterministic: output whose byte order can vary (for example, an unordered collection) can
raise a false positive even when the logical payload did not change.

For marker payloads, the served instance returned by `GetUnsafeProjection` or `GetUnsafeProjectorPayload` is mutated in
place by later in-order unsafe folds. Callers must not retain that instance across calls.

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

Hot-only checkpoint cadence is configurable through `GeneralMultiProjectionActorOptions`
(SEK-G89, related to #1253):

- `HotCatchUpPersistMaxFetchedEvents` defaults to `5000`. It drives both the legacy
  cumulative applied-event modulo (`event_count_checkpoint`, evaluated first) and the
  fetched-event window (`fetched_count_checkpoint`, evaluated second).
- `HotCatchUpPersistMaxIntervalSeconds` defaults to `300` and controls the elapsed-time
  fallback (`time_checkpoint`, evaluated last).
- A value ≤ 0 disables the corresponding trigger(s). Nullable properties with the same
  names in `MultiProjectionPersistenceOverrideOptions`, under
  `ProjectorPersistenceOverrides[projectorName]`, override global values; null inherits them.

The fetched window resets on each persist attempt, whereas the applied count is cumulative.
When fetched and applied counts differ, these triggers can drift out of phase, so the effective
cadence can approach half the configured event threshold. Raising the thresholds reduces
full-state snapshot/blob writes and storage I/O, at the cost of more replay after interruption.
Every hot checkpoint persist also compacts safe history and retained collections (`CompactSafeHistory` / `CompactRetainedCollections`), so larger thresholds, or disabling the triggers, let catch-up memory grow until the next checkpoint or completion.
Disabling both allows completion-only checkpoints; completion still performs final persistence
when catch-up has new events. Defaults preserve the existing 5,000-event / five-minute behavior.
These knobs are independent of live-path `PersistBatchSize` and `PersistIntervalSeconds`.

Cold reads retain their configured segment, applied-count, and interval triggers, plus the
fetched-count fallback. The cold/hot choice comes from read metadata's `UsedCold` value;
a hybrid store with `UsedCold=false` uses the hot-only settings. The summary reports
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

### Optional bounded first-query wait (SEK-G90; #1253 item 5)

`GeneralMultiProjectionActorOptions.FirstQueryCatchUpMaxWaitMs` defaults to `0`.
Values ≤ 0 preserve the blocking first-query barrier. Set a positive value, for example
`1000`, to bound the gate wait after query admission. A nullable
`ProjectorPersistenceOverrides[projectorName].FirstQueryCatchUpMaxWaitMs` overrides it;
null inherits, and zero or negative restores blocking behaviour for that projector.
Choose N well below Orleans `ResponseTimeout` and client/HTTP timeouts. N excludes
activation and request queuing; it is not a total response-time guarantee.

With this option enabled, the first query after activation joins the activation's background
catch-up and the gate settles only after that run completes. By default, completion requires
`MaxConsecutiveEmptyBatches` (5) empty batches at the catch-up interval (1 s). Even a short
tail can therefore take about 5 s before the gate settles, while the default in-call path
takes milliseconds. Choose N accordingly (for example, ≥ 10 s, within the timeouts above),
or expect an initial "catching up" response for a small N. Reducing this latency is a follow-up.

On a fresh activation against an empty event store, a small N can still return the
catching-up error until idle settlement completes (about `MaxConsecutiveEmptyBatches`
× the catch-up interval). Concurrent polls are serialized by the non-reentrant grain;
each poll waits up to N ms after admission.

Only the generic activation arm without a pending durable rebuild marker is eligible,
including activation after snapshot restore failure and host recreation. Tombstones keep
SEK-G85 fail-closed behaviour; durable markers keep SEK-G18 behaviour. Checkpoint mutation,
operator reset/rebuild and in-activation host recreation re-arms remain blocking. Any
re-arm ends the bounded episode; extending bounded waits to those sites is a follow-up.

The query observes the activation's in-flight timer catch-up, or kicks a shared incremental
restart (`forceFull: false`) and background gate settlement. It never performs the full
replay inside an eligible query. Timer batches interleave, but global catch-up concurrency
limits can skip batches, so progress within N is not guaranteed. When the gate settles,
queries succeed; otherwise state/snapshot return `ResultBox.Error`, while scalar/list queries
throw `InvalidOperationException` with `MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix`
(`Projection catch-up is in progress:`), current/target position and events processed.
A later query succeeds after background completion. Live projection faults take precedence
(SEK-G14), including faults discovered during the wait. The fail-closed query does not
change `LastError`; actual background failures still can.

`GetStatusAsync` exposes `FirstQueryCatchUpPending`, `CatchUpTargetPosition` (possibly null
before the first batch), and `LastBackgroundCatchUpError` (message and UTC timestamp, scoped
to the episode generation). It can remain visible in status after a successful settle until
the next arm. The last background failure is also included in the catching-up error, so an
outage remains visible. The appended status constructor parameters have defaults.
`MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix` (`Projection rebuild is pending:`)
covers SEK-G18 and SEK-G85; `checkpoint tombstone` identifies the latter.

SEK-G21's authoritative start-position contract still holds. A background settle can use
an authoritative head read before the query arrived, so successful opt-in queries have
ordinary projection lag semantics, rather than guaranteed freshness at admission.
The existing 30-second `waitForCatchUp` helpers are unchanged.

The constants identify the start of the grain-generated message. Transport or ResultBox
wrappers may prepend text, so hosts must match with `Contains(prefix, StringComparison.Ordinal)`.
When catch-up is inactive and no initiation or settlement is in flight, a poll schedules the
shared background Ensure immediately. An already refreshed host can settle within N without
waiting for the timer's empty-batch threshold; the query never runs Ensure inline.

A minimal ASP.NET Core host mapping (adapt the error source to your query surface):

```csharp
using Sekiban.Dcb.Orleans.Grains;

// Map both ResultBox.GetException() and thrown scalar/list query exceptions.
static IResult MapProjectionError(Exception error, HttpResponse response)
{
    if (error.Message.Contains(MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix,
            StringComparison.Ordinal) ||
        error.Message.Contains(MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix,
            StringComparison.Ordinal))
    {
        response.Headers["Retry-After"] = "2";
        return Results.Problem(statusCode: 503, detail: error.Message);
    }
    return Results.Problem(statusCode: 500);
}
```
