# Storage Providers - Azure and AWS Support

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md) (You are here)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Cold Events and Catch-up](19_cold_events.md)
> - [Materialized View Basics](20_materialized_view.md)

DCB supports multiple cloud platforms for event persistence and projection snapshots. This guide covers configuration for both Azure and AWS.

## Platform Comparison

| Component | Azure | AWS |
|-----------|-------|-----|
| Event Store | Postgres / Cosmos DB | DynamoDB |
| Snapshot Storage | Azure Blob Storage | Amazon S3 |
| Orleans Clustering | Azure Table / Cosmos DB | RDS PostgreSQL |
| Orleans Streams | Azure Queue | Amazon SQS |

---

## Azure: Postgres Event Store

Package: `Sekiban.Dcb.Postgres` (`src/Sekiban.Dcb.Postgres`). Key tables:

- `dcb_events` – event payload, metadata, tags (JSONB)
- `dcb_tags` – tag → event linkage for tag-sliced queries

```csharp
builder.Services.AddSekibanDcbPostgres(configuration);
// or specify connection string directly
builder.Services.AddSekibanDcbPostgres("Host=localhost;Database=sekiban_dcb;Username=postgres;Password=postgres");
```

Run migrations with `Sekiban.Dcb.Postgres.MigrationHost` or let Aspire run the initializer in development.

## Azure: Cosmos DB Event Store

Package: `Sekiban.Dcb.CosmosDb` (`src/Sekiban.Dcb.CosmosDb`). Containers:

- `events` – partitioned by `/pk`
- `tags` – partitioned by `/pk`
- `multiProjectionStates` – partitioned by `/pk`

```csharp
services.AddSekibanDcbCosmosDbWithAspire();
// falls back to ConnectionStrings:SekibanDcbCosmos if Aspire client not found
```

## Azure: Blob Storage Snapshots

Package: `Sekiban.Dcb.BlobStorage.AzureStorage` (`src/Sekiban.Dcb.BlobStorage.AzureStorage`)

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new AzureBlobStorageSnapshotAccessor(
        sp.GetRequiredKeyedService<BlobServiceClient>("MultiProjectionOffload"),
        "multiprojection-snapshots"));
```

---

## AWS: DynamoDB Event Store

Package: `Sekiban.Dcb.DynamoDB` (`src/Sekiban.Dcb.DynamoDB`)

DynamoDB stores events with auto-table creation. Tables are created automatically on first write:

- `{prefix}_events` – event payload with SortableUniqueId as sort key
- Tag indexing via GSI for efficient tag queries

```csharp
builder.Services.AddSekibanDcbDynamoDb(options =>
{
    options.Region = "us-west-1";
    options.TablePrefix = "myapp";
});
```

### Configuration

```json
{
  "Sekiban": {
    "Database": "dynamodb"
  },
  "AWS_REGION": "us-west-1",
  "DYNAMODB_TABLE_PREFIX": "myapp"
}
```

## AWS: S3 Snapshot Storage

Package: `Sekiban.Dcb.BlobStorage.S3` (`src/Sekiban.Dcb.BlobStorage.S3`)

```csharp
services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
    new S3BlobStorageSnapshotAccessor(
        sp.GetRequiredService<IAmazonS3>(),
        "multiprojection-snapshots-bucket"));
```

---

## Configuration Tips

- Set `Sekiban:Database` to `postgres`, `cosmos`, or `dynamodb` to select the backend
- For Aspire development, use memory-based storage for rapid iteration
- Use managed identities (Azure) or IAM roles (AWS) for production credentials

## Operational Considerations

### Azure
- Monitor index usage on `dcb_tags` (Postgres) or RU consumption on Cosmos `tags` container
- Rotate secrets using Azure Key Vault

### AWS
- Monitor DynamoDB read/write capacity and throttling
- Use AWS Secrets Manager for RDS credentials
- S3 lifecycle policies can manage snapshot retention

## Storage Provider Summary

| Provider | Package | Offload Package | Status |
|----------|---------|-----------------|--------|
| Postgres | `Sekiban.Dcb.Postgres` | N/A (no limit) | Production |
| Cosmos DB | `Sekiban.Dcb.CosmosDb` | `Sekiban.Dcb.BlobStorage.AzureStorage` | Production |
| DynamoDB | `Sekiban.Dcb.DynamoDB` | `Sekiban.Dcb.BlobStorage.S3` | Production |
| SQLite | `Sekiban.Dcb.Sqlite` | N/A | Development |

## Tagged cold-rebuild streaming (SEK-G53)

Cold tag-state rebuilds can consume the additive `IStreamingTaggedSerializableEventStore` capability. It leaves the
frozen `IEventStore.ReadSerializableEventsByTagAsync` list API unchanged, so existing providers and downstream stores
continue to use the list fallback without recompilation.

- Postgres and SQLite stream the ordered tag query directly into the callback. `since` is exclusive and the optional
  captured `until` head is inclusive; both bounds are pushed into the provider query.
- The callback contract emits strictly increasing ordinal `SortableUniqueId` values. Tag-state consumers reject a
  decreasing value before publishing a rebuilt state; an equal value follows the established duplicate-skip policy.
- `InMemory` and the in-process executor stores implement the callback shape for parity tests. They are not a
  bounded-memory production provider. Cosmos DB and DynamoDB implement the same native capability without changing
  their compatible list readers:
  - Cosmos DB pages the ordered tag index and runs an ordinal sliding window of event point reads. The separate
    `MaxConcurrentTaggedStreamPointReads` option defaults to **8**, is validated from **1 through 64**, and may not
    exceed the effective tag-index page cap (`MaxItemCountPerPage` when explicitly configured, or 100 for the legacy
    SDK-default sentinel); completed reads wait behind the queue head, so a slow or failed head never publishes a later
    event. A failed head cancels and observes already-issued reads. Its aggregate callback
    (`TaggedStreamTelemetryCallback`) and `Sekiban.Dcb.CosmosDb` meter report only bounded page/read/RU/429 values
    (`sekiban.dcb.cosmos.tag_stream.index_pages`, `.point_reads`, `.request_charge`, and `.throttles`) — never raw
    tag strings or event ids. This is deliberately **not** an RU-sublinear read: budget for one ordered tag-index scan
    plus approximately one event point read per referenced event; the window limits in-flight work and memory, not
    total RU.
  - DynamoDB pushes `since` (exclusive) and `until` (inclusive) into the tag row sort-key condition, reads one query
    page at a time, reads one bounded `BatchGetItem` chunk at a time, and reorders the unordered BatchGet response by
    the query references before invoking the callback. `ReadProgressCallback` and the optional
    `TaggedStreamTelemetryCallback` expose page/chunk and consumed-capacity aggregates. The implementation keeps at
    most one reference page plus one event-body chunk and forwards the caller cancellation token to Query, BatchGet,
    and retry delay operations.
- Capability selection is fail-closed: a store must implement the optional interface **and** declare native tagged
  streaming through the live-instance descriptor. `HybridEventStore` forwards only a verified hot-store stream and
  returns an unsupported `ResultBox` without reading its hot list API otherwise.
- **Optional tag-state cancellation entry paths (SEK-G55).** Existing token-less members remain unchanged. Callers
  that need cancellation can use `ISekibanExecutor.GetTagStateAsync(TagStateId, CancellationToken)` (including its
  generic convenience form), `ITagStateActorCommon.GetStateAsync(CancellationToken)`, the Core/SQLite
  `TagStateService.ProjectTagStateAsync` overloads, or the Orleans `ITagStateGrain.GetStateAsync(CancellationToken)` /
  `GetTagStateAsync(CancellationToken)` grain methods. The executor and in-process actor additions are default
  interface fallbacks to their token-less members, so a downstream implementation compiled before SEK-G55 keeps
  working but does **not** thereby promise cancellation. Sekiban's built-in implementations override the fallback and
  pass the caller token to the native stream. The Orleans members are ordinary grain methods rather than default
  interface members so Orleans 10 can dispatch post-request cancellation; no `Reentrant`, `AlwaysInterleave`, or
  `GrainCancellationToken` is required.
- A streaming provider receives the exact caller token and stops before its next row/callback once cancellation is
  observed. The frozen list fallback cannot interrupt its already-started provider read; it observes cancellation
  immediately after that read and before each projected event. A cancelled rebuild discards its local fold and checks
  again immediately before actor/grain cache publication, so cancellation observed before a write starts produces no
  cache write or successful partial result (a write already in progress is not rolled back).
- `WithResult` exposes cancellation as a `ResultBox.Error` holding `OperationCanceledException`; `WithoutResult`,
  actor, grain, and service callers observe `OperationCanceledException`. `GetLatestTagState*` intentionally has no
  cosmetic token overload because its frozen `IEventStore` read has no cancellation parameter.

## Passive projection status registry (SEK-G24 / dcb-v10.10.0)

The passive `IProjectionStatusStore` is registered alongside each provider's projection-state store, and the
`IProjectionStatusReader` composes its rows with event-store counts without resolving a grain. The sample is explicitly
best effort: one denominator is sampled per service per sampling window (five seconds by default), remaining counts are
taken after each distinct traversed cursor with bounded parallelism, and `SampledAtUtc` identifies the sample window.
The CAS row identity is `(ServiceId, ProjectorName, ProjectorVersion, ClusterId)`; `ActivationId` is retained as row
data. More than one fresh row across clusters for a `(ProjectorName, ProjectorVersion)` is reported as a conflict, and
providers reject stale activation replacements instead of silently last-write-wins updating a different row.

Storage layout and upgrade behavior:

- PostgreSQL and SQLite create a dedicated `dcb_projection_statuses` table automatically, including on an existing
  database. Heartbeats use an atomic expected-sequence CAS.
- Cosmos DB and DynamoDB co-locate status rows with projection snapshots and mark them with
  `documentType = "projectionStatus"`. Snapshot list, delete, scan, and latest-version queries accept legacy
  discriminator-less snapshots but exclude status rows.
- DynamoDB status listing intentionally uses a bounded filtered scan in this slice; add a dedicated status GSI only if
  fleet size makes that escalation worthwhile.
- Status reads and count sampling are read-only with respect to projection state; no grain activation is required.

The serialized surface is the new `ISerializedProjectionStatusReader` with a V1 envelope. Its `ServiceId` is always
bound from the server-side provider. Hosts should keep the endpoint absent or protected by an explicit operator policy
by default, for example:

```csharp
app.MapGet("/ops/projection-status", async (ISerializedProjectionStatusReader reader) =>
        Results.Bytes((await reader.ReadSerializedAsync()).GetValue(), "application/json"))
   .RequireAuthorization("ProjectionStatusOperator");
```

Never use `AllowAnonymous` for this surface. `ISerializedSekibanDcbExecutor` remains untouched. This is the
dcb-v10.10.0 release-note entry for SEK-G24.

### Projection-status heartbeat recovery (SEK-G35 / dcb-v10.16.0)

Projection-status heartbeats now pin the full writer identity at activation: service, projector name, projector
version, and cluster. A host rebuild during a rolling deployment continues to write the version captured by that
activation instead of silently moving the row to the newly registered host version. A new activation naturally uses
the new version.

The expected-sequence CAS remains fail-closed on every provider. In particular, PostgreSQL and SQLite no longer have
an unreachable update path after the initial insert: an update is attempted only when the physical row exists, and a
missing row with a nonzero expected sequence is rejected. The next scheduled heartbeat rebases its local fence and
may perform the normal sequence-zero create; it never inserts unconditionally in the same failed operation. Cosmos
DB continues to use its pinned document identity and provider preconditions for the same contract.

The original serialized V1 envelope is frozen. Operators that need rolling-deployment diagnostics can request the
additive V2 reader envelope, which reports the expected and observed projector versions and whether a row is current,
version-mismatched, or stale/orphaned. These diagnostics are observational: no provider automatically deletes rows
from older versions or other clusters. Apply an explicit retention policy only after confirming that the rows are no
longer useful for rollout or incident analysis.

### Row timestamp and independent version match (SEK-G36 / dcb-v10.17.0)

`ProjectionStatusSnapshot.RecordedAtUtc` is the exact timestamp committed with the heartbeat row. It is not derived
from `SampledAtUtc`, which remains only the reader's best-effort observation time, and it is not a cross-row tie
breaker. The in-process snapshot exposes both `RecordedAtUtc` and the independent
`ProjectionStatusVersionMatch` (`Unknown = 0`, `Match = 1`, `Mismatch = 2`). V1 bytes remain frozen: the new facts
are surfaced by the additive V2 wrapper, whose nested `Snapshot` remains V1-shaped.

Use this five-step consumer rule:

1. Use `VersionMatch` to identify expected-version candidates, independently of freshness: null, empty, or
   whitespace expected values are `Unknown`;
   equality is ordinal and case-sensitive; all other values are `Mismatch`.
2. Use `IsFresh` to report liveness separately. It is true only when both the committed `RecordedAtUtc` is inside the freshness
   window and the optional lease has not expired.
3. When the expected version is `Unknown`, preserve all candidates: do not infer an expectation, fold rows, or
   select one candidate.
4. Preserve same-version observations across clusters and honor the existing conflict signal; do not select one by
   timestamp. A fresh mismatch is still a fresh observation, and two fresh matched rows are still a conflict.
5. Use `RecordedAtUtc` for age display and diagnosis, never as a replacement for expected-version selection. It is
   not a cross-row tie breaker.

Request-property caution: supply `ExpectedProjectorVersion` only when the caller has an expected version to compare.
The V2 `ProjectorVersion` versus `ExpectedProjectorVersion` request-precedence rule is intentionally pending, so do
not set both request properties together.

Escalate, select, retain, or clean up only through explicit consumer policy. A stale mismatch is an orphan
*candidate*, never a deletion authorization; dcb does not fold, filter, declare `IsOrphan`, or delete rows.

Three tempting shortcuts are not authority rules: do not take the ordinal maximum `ProjectorVersion` (`1.0.9` sorts
above `1.0.10`); do not take the maximum `Sequence` (it is a per-row CAS fence, not comparable across rows: observed
in aic dev, orphan `5884` > current `1583`); and do not take the largest `LastAppliedSortableUniqueId` (a current new
version can still be catching up). Use the two independent axes and the caller's explicit policy instead.

## Consistency Contract

This section documents the actual atomicity guarantees of `IEventStore.WriteSerializableEventsAsync` / `WriteEventsAsync` per provider. The two-phase Cosmos write design itself is unchanged; what has changed is how a failure of that write is handled.

### Postgres — current guarantee

`PostgresEventStore.WriteEventsAsync` writes event rows and tag rows inside a single database transaction. Both **event-set atomicity** (all events in a `WriteEventsAsync` call commit, or none do) and **event/tag atomicity** (an event is never visible without its tag rows) are guaranteed.

### Cosmos DB — current guarantee

`CosmosDbEventStore.WriteSerializableEventsAsync` performs a two-phase write with **no transaction spanning the two phases**:

1. Event documents are created in parallel (`CreateItemAsync`), one per event, partitioned by `{serviceId}|{eventId}`.
2. Tag rows are then written via per-tag-partition `TransactionalBatch` (`{serviceId}|{tag}`).

This has two consequences today:

- **Event/tag crash window**: a crash or host termination between phase 1 and phase 2 (or mid-way through the phase 2 tag batches) leaves durable events without their tag rows. Those events are visible via `ReadAllEventsAsync`, but invisible to `ReadEventsByTagAsync`, tag projectors, and `GetLatestTagAsync` — which also feeds `GeneralTagConsistentActor`'s optimistic-concurrency baseline. A crash is not an in-process failure, so no policy below can prevent this window; only a repair pass can close it.
- **Multi-event partial visibility**: a multi-event `WriteEventsAsync` call can fail partway through its parallel event creates. Successfully created events become immediately visible to all-events readers even though sibling events from the same call may never get written. This is now reported as a `CosmosPartialEventWriteException` naming both the visible and the failed event ids, and **nothing is deleted** in response.

Tag rows are **derived deterministically** from the (event, tag) pair — `pk = {serviceId}|{tag}`, `id = {eventId}`, and the remaining fields from the event document, which carries the complete `tags` array (see `Models/CosmosEvent.cs` and `Tags/CosmosTagIdentity.cs`). Two things follow. Re-executing a tag write is safe: it re-derives identical rows, accepts the ones already present, and fills in the ones a partial write missed. And the `tags` container is a **derivable index** that can always be rebuilt from the `events` container. An existing tag row whose content disagrees with the event raises `CosmosTagIndexCorruptionException` and is never overwritten; that error is **not retryable** — the same content is derived on every attempt.

#### Write failure policy

`CosmosDbEventStoreOptions.WriteFailurePolicy` selects what happens when the tag-write phase fails in-process:

| Policy | Behavior |
|---|---|
| `Compatible` (**default**) | The behavior of earlier releases: the tag write is not retried, and if the (now `[Obsolete]`) `TryRollbackOnFailure` is set — it defaults to `true` — the already-written event documents are best-effort **deleted**. |
| `RollForward` (opt-in) | The tag write is **retried** (exponential backoff with jitter, an overall deadline, Cosmos `Retry-After` honored on 429, `CancellationToken` observed), converging on whatever rows a partial write left missing. Events are **never deleted**. If the retries run out, `CosmosTagWriteExhaustedException` names the events whose tag rows may be missing, and those events stay durable for a later repair. |

A server-sent `Retry-After` is honored in full: `MaxBackoff` caps the client's own backoff curve, not the server's instruction, so a `Retry-After` longer than `MaxBackoff` is **not** shortened — retrying before the server is ready would only earn another 429. `MaxTotalDuration` still bounds the whole sequence: if honoring the hint would cross the deadline, the write stops with `CosmosTagWriteExhaustedException` rather than retrying early.

**Rollback deletes durable events that all-events consumers — multi-projections above all — may already have read**, which contaminates their state irreversibly, and it only runs on an in-process exception, so it never runs after a crash. `RollForward` is the recommended setting for new deployments:

```csharp
services.AddSekibanDcbCosmosDb(
    configuration,
    options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);
```

The default stays `Compatible` through the current release line so that **upgrading the package alone changes nothing** for an existing deployment. It flips to `RollForward` only at a major version boundary, with a documented migration.

#### Telemetry

The `Sekiban.Dcb.CosmosDb` meter (`CosmosDbTelemetry.MeterName`) publishes `sekiban.dcb.cosmos.tag_write.failures` (label `reason`: `transient` | `corruption`), `sekiban.dcb.cosmos.tag_write.retries`, `sekiban.dcb.cosmos.tag_write.retry_outcomes` (label `outcome`: `recovered` | `exhausted`), and `sekiban.dcb.cosmos.event_write.partial_failures`. Metric labels are drawn from small fixed sets; raw event ids and tag strings are unbounded and therefore appear only in the structured logs and in the exceptions above.

#### Tag index repair (`CosmosDbTagRepairService`)

Because a tag row is fully derivable from its event, the tags container can be rebuilt from the events container. `CosmosDbTagRepairService` is the operator surface for that: it scans events over a `sortableUniqueId` range, derives the expected row for each `(event, tag)` pair, and creates the ones that are missing.

It is **strictly non-destructive**. It only ever creates rows that do not exist — it never deletes, rewrites, or canonicalizes one. It is deliberately not part of `IEventStore`: run it as an operator job, never expose it from a request path.

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagRepair();   // opt-in; not registered by AddSekibanDcbCosmosDb alone

// In an operator-only job:
var repair = await factory.CreateAsync(serviceId);   // one instance == one (serviceId, events, tags) lineage

// Always look before you write.
var report = await repair.RepairAsync(new CosmosTagRepairOptions
{
    DryRun = true,                                    // the default
    ToSortableUniqueIdInclusive = lastSettledId,      // pin the upper bound; live writes repair themselves
    MaxEventsToScan = 10_000,
});

// Then repair, resuming across bounded runs.
string? checkpoint = null;
do
{
    var run = await repair.RepairAsync(new CosmosTagRepairOptions
    {
        DryRun = false,
        ToSortableUniqueIdInclusive = lastSettledId,
        Checkpoint = checkpoint,
    }, cancellationToken);

    checkpoint = run.HasMore ? run.Checkpoint : null;
} while (checkpoint != null);
```

The service can also be constructed manually — `new CosmosDbTagRepairServiceFactory(context, containerResolver)` — for hosts that build their stores through a custom factory instead of DI. Either way, an instance is bound to one `(serviceId, events container, tags container)` lineage at construction, so repairing across lineages is structurally impossible rather than merely discouraged.

**Report categories.** Every `(event, tag)` key lands in exactly one:

| Category | Meaning | Does repair write? |
|---|---|---|
| `Present` | The derived row exists and matches the event. | No |
| `Missing` | Nothing indexes this pair. | **Yes** — this is the only category it writes |
| `LegacyPresent` | A row written before the deterministic-id scheme indexes this pair. Its random id and wall-clock `createdAt` are expected differences, reported as migration metadata. | No — and the legacy row is never touched |
| `Duplicate` | More than one legacy row indexes this pair (residue of a pre-deterministic re-execution). | No — reported only; reducing them is destructive and out of scope |
| `Corrupt` | A row occupies this pair but disagrees with the event (`sortableUniqueId`, `eventType`, or `tagGroup` drift; or a row at the derived id whose content differs). | No — never overwritten |
| `Overflow` | More rows index this pair than `MaxRowsPerKey` allowed the scan to examine. | No — raise the cap to look deeper |

**Operational guidance.**

- **Least privilege**: the job needs read on the events container and *create* on the tags container. It never deletes or replaces, so a Cosmos role without `deleteItem`/`replaceItem` on the tags container is sufficient — and is the recommended way to enforce non-destructiveness at the platform level rather than trusting the code.
- **RU cost**: the events scan is cross-partition (events are partitioned per event), and each `(event, tag)` key costs one partition-confined query against the tags container. So RU scales with *keys*, not events: roughly `events × tags-per-event` point-ish queries plus the scan itself. `MaxParallelism` (default 4) is the RU-rate dial; `MaxEventsToScan` (default 10,000) bounds one run's total cost. Throttling is expected — the service honors Cosmos `Retry-After` in full rather than retrying early.
- **Pin the upper bound**: set `ToSortableUniqueIdInclusive` to an event you know is settled. Events written while the scan runs are indexed by the write path itself; the repair is for crash residue, not for live traffic.
- **Concurrency**: a run is safe alongside live writes and alongside another run. If a normal write lands the very row a run classified as missing, the run recognizes it as the row it was about to write and moves on — no duplicate, no error.
- **Consistency caveat**: reads issued immediately after a repair may still observe stale state depending on the Cosmos consistency level configured on the account. Under session or eventual consistency, a tag-scoped read from a different session can lag the repair's writes.

#### Automatic sweep (`AddSekibanDcbCosmosDbTagSweep`) — opt-in

The repair service only runs when an operator runs it, so routine crash residue sits unrepaired until somebody notices. The sweep closes that gap: it runs the repair over a recent window shortly after startup, and optionally on an interval.

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagSweep(sweep =>
{
    sweep.Enabled = true;                          // off by default — see below
    sweep.Window = TimeSpan.FromHours(24);         // crash residue is recent; a full backfill is a manual job
    sweep.Interval = TimeSpan.FromHours(6);        // omit to sweep only at startup
    sweep.MaxParallelism = 2;                      // yield to live traffic
    sweep.MaxEventsPerRun = 10_000;                // bounds one run's RU cost
    sweep.RunBudget = TimeSpan.FromMinutes(5);     // a run that overruns resumes next turn
});
```

A run that hits `RunBudget` keeps the progress it settled: its checkpoint advances past the events it finished, and the next turn starts from there. So a budget too tight to finish the window in one turn still makes forward progress each turn instead of re-scanning the same prefix forever. A host shutdown is not a budget overrun — it simply stops the sweep, and nothing is persisted.

**Opt-in twice over.** `AddSekibanDcbCosmosDb` does not register the sweep, and even when registered it stays inert until `Enabled` is set. Referencing or upgrading the package adds **no hosted service, no network scan, no startup delay, and no configuration you must fill in**. Startup is never blocked: the sweep runs in the background, and `RunBudget` bounds any single run. A failing sweep is logged and retried on its next turn — it never takes the host down.

Options and checkpoints are **per lineage**. `ServiceIds` selects which lineages to sweep (empty means the host's own service id); each is swept independently with its own window and its own resume point.

**It cannot be configured into destructiveness.** The sweep's only route to storage is the repair service, whose store cannot express a delete, a replace, or an upsert. It backfills missing rows and classifies everything else — repeated sweeps over `LegacyPresent` or `Duplicate` sets create **zero** rows and delete **zero** rows. `Corrupt` and `Overflow` are surfaced via telemetry and logs without any attempt to migrate or reduce them. There is no setting, and no code path, that changes this.

**Before enabling an interval**: run a manual **dry run** and watch the RU cost (see the RU guidance above). Then keep `MaxParallelism` at 2–4 and schedule the interval to land in a quiet window.

**Replicated services**: every replica starts at roughly the same moment, so they would all sweep at once and spike RU together. `MaxStartupJitter` (default 30s) spreads the startup runs apart. For an interval sweep across many replicas, prefer electing a leader (or taking a lease) and sweeping from one instance — jitter alone thins a stampede, it does not prevent duplicated work. Duplicated work is *safe* (runs are idempotent and overlap-safe with each other and with live writes); it is just RU you did not need to spend.

**Sweep telemetry** (meter `Sekiban.Dcb.CosmosDb`): `sekiban.dcb.cosmos.tag_sweep.runs` (label `outcome`: `completed` | `budget_exhausted` | `failed`), `sekiban.dcb.cosmos.tag_sweep.repaired_rows`, `sekiban.dcb.cosmos.tag_sweep.corrupt_keys`, `sekiban.dcb.cosmos.tag_sweep.overflow_keys`.

#### What the sweep does NOT guarantee

Read this before relying on it. The sweep is **eventual repair, not a safety net**:

- **It does not gate tag readers.** Nothing waits for a sweep. `ReadEventsByTagAsync`, tag projectors, and `GetLatestTagAsync` serve whatever is in the tags container at the moment they are called.
- **A missing-tag window remains.** A crash can leave residue at any moment; the sweep only reaches it on its next run. Between the crash and that run, the affected events are visible to all-events readers and invisible to tag-scoped ones.
- **The tag-consistent actor's baseline can be regressed for that whole window.** `GeneralTagConsistentActor` rebuilds its optimistic-concurrency baseline from the tags container, so a missing tag row silently lowers it until repair lands. This is a known limitation, not an oversight.
- **Post-repair reads can still be stale**, depending on the Cosmos consistency level configured on the account.

Readiness gating, read-time verification, and a commit protocol that would close these windows are **future work**, deliberately out of scope here. If your workload cannot tolerate the window — money-sensitive workflows above all — **use the Postgres provider**, which gives you event/tag atomicity in a single transaction and has none of these gaps.

#### Legacy tag-row migration (destructive, operator-only)

Rows written before the deterministic-id scheme sit at a random document id. **They work.** The repair service recognizes them by semantic key, tag reads find them, and nothing about correctness depends on migrating them. This tool exists to tidy them up — reducing the rows of an `(event, tag)` to the single canonical row — and it does that by **deleting documents**. It is the only thing in Sekiban that does.

It is not registered by `AddSekibanDcbCosmosDb`, it is **not reachable from the automatic sweep**, and it never runs by itself.

**The service API** ships in the `Sekiban.Dcb.CosmosDb` package: register the factory with `AddSekibanDcbCosmosDbLegacyTagMigration()`, then call `PlanAsync` and `ApplyAsync(plan, options)`.

**Use the service API.** It is the supported path, it ships in the package you already reference, and it is what the CLI calls anyway.

**The CLI is not distributed, and no released tag contains it yet.** `tools/SekibanDcbTagMigration` is not packaged, not published, and not a `dotnet tool` — no release produces an executable you can install. It was added *after* the most recent release tag, so checking out any published `dcb-v*` tag gives you a tree in which the tool does not exist and `dotnet run --project tools/SekibanDcbTagMigration` cannot work. It is a thin front-end over the same service (no destructive logic of its own, and it could not have any: the seam that expresses a tag-row delete is `internal` to `Sekiban.Dcb.CosmosDb`, so no other assembly can issue one).

If you nonetheless want to run it, **run it from a source revision you have explicitly reviewed** — and check that the revision actually contains it *before* you check it out:

```bash
git clone https://github.com/J-Tech-Japan/Sekiban.git
cd Sekiban

# Does this ref contain the tool at all? (Substitute the ref you intend to use.)
REF=main
git cat-file -e "$REF:tools/SekibanDcbTagMigration/SekibanDcbTagMigration.csproj" \
  && echo "tool present at $REF" \
  || echo "tool ABSENT at $REF — do not use this ref"

git checkout "$REF"
```

Once a release tag does contain the tool, prefer that tag over a branch, and use the same existence check to confirm it before you trust it. **Do not verify a release by grepping `<PackageVersion>` out of the csproj** — the value in the tagged source is a build-time placeholder, not the version that was published.

Why any of this care: a tool built from a revision other than the code that wrote your rows will happily produce a plan describing a world you do not have — and that plan authorizes deletions.

With a reviewed checkout, the two-step flow is:

```bash
# Plan. Read-only. Writes an artifact naming exactly which rows would die.
dotnet run --project tools/SekibanDcbTagMigration -- plan \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json

# READ IT. This is the point of the two-step flow.

# Apply. Refuses without --confirm and --backup.
dotnet run --project tools/SekibanDcbTagMigration -- apply \
  --connection "<cs>" --database SekibanDcb --service-id <id> \
  --plan tag-migration-plan.json --backup removed-rows.json --confirm
```

**What stops a mistake.** Every one of these refuses *before* touching a document:

| Gate | Behavior |
|---|---|
| No plan | `ApplyAsync` takes a plan and nothing else. You cannot delete rows you were not first shown. |
| No `Confirm` | `CosmosTagMigrationNotAuthorizedException`. The flag has no permissive default. |
| No backup writer | `CosmosTagMigrationNotAuthorizedException`. Cosmos has no undo, so the export *is* the recovery path. |
| Plan altered since it was produced | Its fingerprint no longer matches its contents → `CosmosTagMigrationPlanRejectedException`. An artifact that was not reviewed authorizes nothing. |
| Plan from another lineage | Refused. An instance is bound to one `(serviceId, events, tags)` at construction. |
| Backup write fails | Nothing is deleted. The backup is written first, before the first delete, on purpose. |

**Survivor policy.** The SEK-G2 deterministic-id row always wins — the one whose document id is the event id, which is what the write path produces today and what every future write would produce. If no such row exists, the migration **creates it from the event** (so the survivor's content is what the write path would have written — no legacy quirk outlives the migration) *before* removing the legacy rows, so the key is never left unindexed even for an instant. Legacy rows are never promoted, only removed, so there is no tiebreak to get wrong. Planning the same unchanged world twice produces a byte-identical artifact.

**Concurrency — one transaction, not a proof followed by a delete.** Every row of an `(event, tag)` lives in the same partition, so the reduce is a **single Cosmos transactional batch**: the canonical survivor is conditioned (created when the plan found none, or replaced-if-match on its exact version when it found one) and every victim is deleted-if-match, all in one atomic boundary.

That shape is the point. Proving the survivor with a read and *then* deleting is a check followed by a use, and the world can move in between: the survivor could be removed after the proof and the deletes would still commit, leaving the key indexed by nothing. Re-reading more often narrows that window; it does not close it. Here there is no window — **either the key ends up canonical, or nothing about it changed**:

- survivor appeared / vanished / was rewritten since the plan → transaction refused → **not one victim deleted** → `StaleSurvivor`
- any victim moved since the plan → transaction refused → **not one victim deleted, not even the ones that had not moved** → `LostRaceContentChanged`
- the audit never claims a row died in a transaction that did not commit.

A key needing more than a transaction can carry (100 operations: 1 survivor + 99 victims) is **refused at plan time** — splitting it across transactions would put the gap straight back.

**What it will not touch.** A row that disagrees with its event is not a duplicate — it is corruption, and this tool does not get to decide what to do about it: reported as `Skipped`, never deleted. That includes the **canonical row itself**: if the row at the deterministic id disagrees with its event, the whole key is left alone and no action is planned for it. Planning around it would be worse than useless — the run conditions the survivor with a replace, so it would quietly rewrite the corrupt row into what the event says, "fixing" it, and then delete the legacy rows that were the only other record of the key. Same treatment for a key with more rows than the per-key cap (`Overflow`).

**Audit.** Every key produces an audit entry — survivor, rows removed, outcome — including the keys it declined to touch.

**Recovery.** The backup file holds the removed rows as complete documents in the shape the tags container stores them. Restoring is creating them again: no transformation, nothing to reconstruct.

**Still planned (not yet released)**: readiness gating.

### Choosing a provider

Recommend **Postgres** for any workload that requires atomic event/tag visibility or event-set atomicity — for example money-sensitive workflows. Concrete examples in the Sekiban ecosystem:

- [SekibanWasmRuntime](https://github.com/J-Tech-Japan/SekibanWasmRuntime) defaults to Postgres for its event store, with Cosmos as an opt-in.
- SekibanAsAService uses separate management and runtime container lineages; each can select its provider independently, so apply the same Postgres-for-atomicity guidance per container.

## Durability Descriptors and the Production Guard

Names are not evidence. `InMemoryDcbExecutor` reads like a test type, and a production system still registered it as its `ISekibanExecutor`; the one-argument constructor quietly created a private in-memory event store; every command succeeded; and no event ever reached the Cosmos account that was configured, sitting there, correct and empty. Nothing at startup could detect it, because nothing could *ask*.

Now something can.

### The two questions anything can be asked

Every built-in store and executor answers these at runtime, from the live instance — not from its type name, not from an attribute, not from which `Add...` method was called:

| Axis | Values | Who answers |
|---|---|---|
| Storage durability | `Durable` / `Volatile` / `Unknown` + provider name | event stores **and** projection state stores (and their per-service factories) |
| Executor runtime | `DistributedRuntime` / `TestingInProcess` / `Unknown` + runtime name | executors, by asking the actor accessor they actually run on |

Built-ins self-declare: Postgres, Cosmos DB, DynamoDB → `Durable`. InMemory → `Volatile`. Orleans → `DistributedRuntime`. `InMemoryDcbExecutor` → `TestingInProcess`.

Two consequences worth stating, because they are the reason this is resolved at runtime rather than inferred:

- **Sqlite answers `Durable` for a file and `Volatile` for `:memory:`** — same class, same name, opposite guarantee. Only the live instance knows which one it got.
- **A decorator reports where the data actually lands.** `HybridEventStore` wrapping a volatile store is *volatile*. A wrapper cannot launder durability by being wrapped around it.

`Unknown` is not a neutral value. It means the component declined to say, and the guard treats it as unsafe, because silence is not a promise of durability.

### The guard

```csharp
// Opt-in. Nothing registers this for you: a library that decides on its own when your host may not start
// is a library that will one day be wrong about it.
builder.Services.AddSekibanDcbProductionGuard();
```

Call it after the registrations it is meant to check. At startup — after every registration has had its say, before the host serves anything — it resolves what the container *actually built*, asks each thing what it is, logs the banner, and in a Production environment refuses to start the host when:

- the executor is not a `DistributedRuntime` (i.e. it is `TestingInProcess` or `Unknown`), **or**
- either store is not `Durable` (i.e. `Volatile` or `Unknown`).

Outside Production it validates nothing and only logs. Development is unchanged.

### The one override, and the one that does not exist

```csharp
builder.Services.AddSekibanDcbProductionGuard(options =>
{
    options.AllowVolatileStorageInProduction = true;   // storage ONLY
    options.ProductionEnvironmentNames.Add("prod-eu"); // if your real environment is not called "Production"
});
```

`AllowVolatileStorageInProduction` is narrow in two directions, and both are deliberate:

- **Storage only.** It cannot authorise a testing executor — set it, register `InMemoryDcbExecutor` in Production, and the host still refuses to start.
- **`Volatile` only, never `Unknown`.** Setting it means *"I looked at a store that said it was volatile, and I meant it."* A store that says nothing has not given you anything to mean, so `Unknown` stays fail-closed with the override on. The way past `Unknown` is to make the store durable, or to have it implement `IStorageDurabilityDescriptorProvider` so it can say what it is.

There is **no** override that permits a testing executor in Production. Not off by default, not hidden behind a flag: it does not exist. A volatile store in Production can be a decision (a cache-shaped service, a throwaway environment). A test executor in Production is not a decision, it is an accident.

Any override you do turn on is named, by name, in the startup banner at `Warning`, so nobody has to read the deployment to find out.

### The banner

Logged on every start (with `AddSekibanDcbStartupBanner()` if you want the report without the enforcement — sensible for local development, where a volatile store is the point):

```
Sekiban DCB startup. Environment=Production IsProduction=True
  ExecutorType=Sekiban.Dcb.Orleans.OrleansDcbExecutor ExecutorRuntime=DistributedRuntime ExecutorRuntimeName=Orleans
  EventStoreProvider=CosmosDb EventStoreDurability=Durable
  ProjectionStoreProvider=CosmosDb ProjectionStoreDurability=Durable
  Overrides=(none) Enforcing=True
```

It never logs a connection string. A banner that leaks a secret into every log sink would be a worse bug than the one it exists to prevent.

### Where the in-memory stack lives now

The volatile stores and the in-process executor now have a home of their own: `Sekiban.Dcb.Core.Testing`,
`Sekiban.Dcb.WithResult.Testing` and `Sekiban.Dcb.WithoutResult.Testing` (namespace `Sekiban.Dcb.Testing`). A project
that does not reference those packages cannot reach the new `Sekiban.Dcb.Testing` entry points at all — so a runtime
project cannot pick the testing executor up by accident.

The old `Sekiban.Dcb.InMemory` types are **not** removed: they stay public in the runtime packages, they still compile,
they still behave identically, and they are `[Obsolete]` pointing at the new home. So the compiler does not stand
between a runtime project and the *old* names — the descriptors above, and the guard that acts on them, are what do,
and they act on whatever was actually resolved regardless of which name produced it. The old names go away at the next
major.

For local development that behaves like production, use a single-silo localhost Orleans host:
[Localhost Orleans](22_localhost_orleans.md).

### The obsolete constructor

`new InMemoryDcbExecutor(domainTypes)` is `[Obsolete]`. Its behaviour is unchanged — only its silence is deprecated. Pass the store you mean to use:

```csharp
var executor = new InMemoryDcbExecutor(domainTypes, new InMemoryEventStore());
```

so that the store is a decision somebody made, visible in the code that made it.

## Materialized View Storage

Materialized views are currently implemented for PostgreSQL:

- `Sekiban.Dcb.MaterializedView` – core contracts and hosted catch-up worker
- `Sekiban.Dcb.MaterializedView.Postgres` – registry, executor, row access, and table updates
- `Sekiban.Dcb.MaterializedView.Orleans` – grain orchestration and query accessor

This is separate from the main event store package. In the current PoC, a service can store events in one database and
materialized view tables in another PostgreSQL database or schema. See [Materialized View Basics](20_materialized_view.md).

## Conditional (Unique-Key) Append — SEK-G15

The base `IEventStore.WriteSerializableEventsAsync` is unconditional: EventIds are server-generated and the write always lands. When you need "exactly one host performs this" — e.g. a one-time migration fanned out across N hosts — use the **optional** conditional-append contract. It is strictly additive: `IEventStore`, `ICommandContext`, and the existing serialized DTOs are untouched, and nothing changes for stores that do not opt in.

### The contract

- **Optional interface** `IConditionalEventStore.AppendIfUniqueAsync(ConditionalAppendRequest, ...)` — single-event append under a caller-supplied idempotency key. Feature-detected with `is`, exactly like `IStreamingSerializableEventStore`. A store that implements it MUST also implement `IWriteConditionCapabilityProvider` (an architecture test enforces this — there is no silent default pass-through).
- **Outcome machine** (one observable contract): `Appended` / `AlreadyCommittedSameOperation` (both carry a durable **receipt**: winner `EventId`, `SortableUniqueId`, and operation fingerprint) / `KeyReuseConflict` / `ConditionNotSupported`.
- **Executor seams** — opt-in only: new `ExecuteAsync(command, handler, CommandExecutionOptions, ...)` overloads on both facades (existing `ExecuteAsync` overloads unchanged; `ICommandContext` unchanged) and a new versioned `SerializedConditionalCommitRequest` / `CommitSerializableEventConditionallyAsync` on the WASM boundary (the existing positional `SerializedCommitRequest` is untouched).

### Capability discovery (runtime-resolved, fail-closed)

Support is a **runtime capability descriptor**, reused from the G10 pattern — never a type-name check. `WriteConditionKind` distinguishes kinds (`SingleEventUniqueKey` and PostgreSQL-only `ExpectedTagPosition`; `BatchUniqueKey` remains reserved). The descriptor is resolved from the **live** store the container built, and decorators propagate it: a `HybridEventStore` reports exactly what its hot store can enforce (writes land there — it never upgrades on its own authority), and a composite supports a kind only if **every** underlying store does. A store that says nothing supports nothing.

Requesting a conditional append against a store that does not support the kind **fails closed**: `ConditionNotSupportedException` is raised **before** the command handler runs, before any EventId is allocated, before serialization, and before any store call. There is no silent degradation to an unconditional write.

### Operation fingerprint and receipt

Operation identity alone is never proof of "same operation". Each claim persists a **canonical fingerprint** derived (with a length-prefixed, domain-separated SHA-256) from, in order: derivation version, canonicalization version, domain separator, ServiceId, the normalized idempotency key (NFC, trimmed, ≤ 512 UTF-8 bytes), the **authoritative event-type identity**, the **canonical payload**, and the event's tags. The server-generated EventId/SortableUniqueId are **excluded** — a genuine retry allocates fresh ids yet must still be recognised. ServiceId is part of the identity, so a key claimed under one service is never matched under another. Raw keys are never logged or returned; only the opaque fingerprint leaves the layer. The derivation is **versioned** (currently derivation v2 / canonicalization v1) and pinned by golden vectors with literal digests, so any change to the version, domain separator, field order, length-prefix framing, or canonicalization algorithm is a deliberate, test-breaking change.

- **Authoritative event-type identity**: the type is resolved through the domain's registered event types (its CLR `FullName`), not the caller-supplied simple payload name. An unregistered type fails closed before any side effect.
- **Canonical payload (supported shapes)**: the raw payload is deserialized into the resolved type and re-serialized by the domain, then re-emitted as canonical JSON — **object keys recursively ordinal-sorted, array element order preserved, numbers and strings as the domain serializer emits them** (Unicode escaping and property order therefore do not matter; `1` and `1.0` are distinct). This is stable for records/POCOs serialized with System.Text.Json (reflection or source-gen), independent of property declaration order. The supported shape is **enforced programmatically, not merely documented**: before hashing, the payload's *effective* `JsonTypeInfo` graph is validated (cycle-guarded, without mutation, using the actual reflection or source-gen metadata) — the root must be a JSON object, collections must be ordered types (arrays, `List`/`IList`/`IReadOnlyList`, `Collection`/`ReadOnlyCollection`, `ImmutableArray`/`ImmutableList`), leaves must be an allowlist of deterministic primitives, and **no custom converter, non-object/converter-owned type (`JsonTypeInfoKind.None`), set, or dictionary is admitted**. Anything else — including a converter that could emit non-deterministic output — is rejected before any (de)serialization, so it can never yield an unstable or two differing fingerprints. A payload that cannot be deserialized/canonicalized likewise fails closed.
- **Ordered-tag semantics**: tags are ordinal-sorted (order-insensitive), duplicate-significant, and case-sensitive.

Outcomes:

- Same key + **same** fingerprint → `AlreadyCommittedSameOperation`, returning the ORIGINAL winner's receipt; nothing is written.
- Same key + **different** fingerprint → `KeyReuseConflict`. When a real provider surfaces a unique-violation, that provider exception may be preserved as the inner cause; a conflict discovered by read has no provider exception and none is fabricated.
- Cannot canonicalize (unregistered type, undeserializable payload) → fails closed with a typed `OperationCanonicalizationException`. This failure is **secret-safe**: the underlying converter/deserializer exception — which can embed the raw payload or key in its message, `Data`, or stack — is **discarded**, never chained into the result. The typed exception carries only sanitized metadata (the registered event-type name), no inner exception, and no payload/key.

### The boundary: one durable claim, not exactly-once side effects

The store guarantees **at most one durable claim per key**. That is a storage guarantee, not an exactly-once side-effect guarantee: making an external effect (send an email, call an API) happen exactly once still needs an outbox / idempotency layer on top of the winning claim.

### Reference implementation and provider status

A deterministic in-memory reference lives in the testing package (`Sekiban.Dcb.Testing.InMemoryConditionalEventStore`, never referenced from a runtime project) and implements the full outcome machine. **All four production providers implement it (SEK-G16)** — PostgreSQL, SQLite, Cosmos DB, and DynamoDB — with identical observable semantics. The distinct multi-tag expected-position CAS is PostgreSQL-only (SEK-G40) and is documented below.

### Provider mechanics (SEK-G16)

Every provider produces the identical outcome machine through one shared orchestrator (`ConditionalAppendExecution`). The provider supplies only three primitives — durably write the claim event under a deterministic id using its native uniqueness primitive, read the committed winner back, and (where its event and index rows are NOT written atomically) bring the winner's contracted committed state to convergence — and the orchestrator does normalization, the pre-write fingerprint (fail-closed on an unsupported shape *before* any store call), classification, the committed-state gate, and receipt construction.

**Deterministic storage identity.** The claim event is stored under an EventId derived purely from the key: `EventId = SHA-256(domain-separator ‖ version ‖ ServiceId ‖ normalized-key)` with UUID version/variant bits applied (`ConditionalAppendIdentity`, derivation v1). The caller's random EventId is discarded for the stored claim, so the storage identity is a pure function of `(ServiceId, key)` and the existing per-row/per-item primary key *is* the uniqueness primitive — **no schema migration and no new column/index on any provider**. Because identity is derived, not stored, and the fingerprint is recomputed from persisted event content, a same-operation retry recomputes an identical fingerprint from the stored winner, and a different operation under the same key recomputes a different one.

**Classification is by recomputed fingerprint, never by the raw conflict signal — and the collision must be the *intended* one.** A provider conflict is, on its own, *never* treated as a same-operation success. Each provider maps ONLY the specific constraint/reason that is the deterministic claim collision; an unrelated constraint or an unexpected cancellation reason preserves its original provider failure and is never misrouted to winner classification. On a mapped conflict the orchestrator reads the committed winner back and compares fingerprints; if the winner cannot be read back (an in-doubt/uncommitted claim) it raises a typed retryable `ConditionalAppendInDoubtException` rather than reporting `AlreadyCommittedSameOperation`. The real provider exception is preserved as the diagnostic inner cause on a `KeyReuseConflict` (or on the in-doubt) only when one actually occurred.

| Provider | Uniqueness primitive | Mapped conflict signal (only this is a claim collision) | Winner read-back | Committed-state gate |
|----------|----------------------|--------------------------------------------------------|------------------|----------------------|
| PostgreSQL | `(ServiceId, Id)` primary key; plain transaction (not the retrying execution strategy), event + tag rows in ONE transaction | `DbUpdateException` → `PostgresException` SQLSTATE 23505 **with `ConstraintName == "PK_dcb_events"`** (any other 23505 stays a provider failure) | `AsNoTracking` point read by `(ServiceId, Id)` | none needed — write is atomic |
| SQLite | `(ServiceId, Id)` primary key; new path is a plain `INSERT` under the write lock (legacy `INSERT OR REPLACE` path byte-for-byte unchanged), event + tag rows in ONE transaction | `SqliteException` with **`SqliteExtendedErrorCode == 1555` (SQLITE_CONSTRAINT_PRIMARYKEY)** on the isolated event insert (any other constraint rolls back and propagates) | point read by `(ServiceId, Id)` | none needed — write is atomic |
| Cosmos DB | item id within partition `{serviceId}|{id}`; the event document and its tag rows are written in **separate phases** (NOT atomic) | `CosmosException` 409 Conflict on the event create | consistent point read of the event item | **required** — a same-operation retry idempotently repairs/verifies every tag row (create → on-409 read-back → `ContentEquals`) before AlreadyCommitted; if repair cannot reach committed state the result is typed in-doubt |
| DynamoDB | item primary key `pk = SERVICE#{serviceId}#EVENT#{id}`; **one `TransactWriteItems` holding the event Put (index 0, `attribute_not_exists(pk)`) plus one Put per tag row**, **no `ClientRequestToken`** (so a same-operation retry surfaces the real conflict instead of being idempotency-collapsed). Limits are enforced fail-closed BEFORE the call: ≤ 100 items and no duplicate item key (a `DynamoConditionalAppendLimitException`). | `ConditionalCheckFailedException`, or a `TransactionCanceledException` whose **cancellation reason at index 0** is `ConditionalCheckFailed` (a reason at any other index is a provider failure) | `GetItem` with `ConsistentRead = true` | none needed — the transaction is atomic |

The unconditional write path, `IEventStore`, and every default are unchanged on all four providers; the conditional path is purely additive and is registered alongside the store (`IConditionalEventStore` + `IWriteConditionCapabilityProvider` resolve to the same singleton the container already builds). The per-service factories (`PostgresEventStoreFactory`, `CosmosDbEventStoreFactory`) and the `HybridEventStore` decorator propagate the capability; a composite reports a kind only when every participant does (fail-closed intersection).

**Atomicity is per provider, not uniform.** Postgres/SQLite/DynamoDB write the event and its tag rows in a single transaction, so a committed event always has its tag rows. Cosmos does not — it writes the event document, then the tag rows, so a crash in between leaves a committed event whose tag-scoped visibility is not yet restored. The Cosmos committed-state gate closes exactly that window on the next same-operation attempt; a passing shared outcome test does not by itself prove atomicity on Cosmos.

**Typed retryable in-doubt vs. non-retryable corruption.** When the outcome cannot be resolved — a conflict with no readable winner, an ambiguous cancellation/timeout after a possible durable commit, or a committed state that could not be verified/repaired (transient) — the append fails with a typed `ConditionalAppendInDoubtException` (`IsRetryable == true`; a closed `Reason` enum — `WinnerUnreadableAfterConflict` / `AmbiguousAfterWrite` / `CommittedStateUnverified` — with a stable string `ReasonCode`; the provider name, the ServiceId, and the *derived* EventId; never the raw key or payload). It is a failure the caller retries, not a fifth outcome status; a retry converges (once the winner commits it classifies as `AlreadyCommittedSameOperation`). Distinct from that, when a same-operation retry finds an existing committed index/tag row that **disagrees** with the event (a strict content mismatch, not a missing row), it fails with `ConditionalAppendCommittedStateCorruptionException` (`IsRetryable == false`): the disagreeing row is NEVER overwritten and the failure must NOT be retried indefinitely — it is surfaced for an operator to investigate. Missing rows are repaired; disagreeing rows are corruption. On the WithResult facade both are `ResultBox.Error`; on WithoutResult the guarded boundary rethrows them; the versioned serialized boundary preserves the typed exception verbatim (no generic wrap). A post-durable-commit cancellation/timeout is first resolved by an authoritative read + fingerprint (+ committed-state verification) to `AlreadyCommittedSameOperation` when possible, and only otherwise surfaced as in-doubt. The reason codes are a closed set — a caller cannot construct an arbitrary reason.

### Recipe: a one-time migration fanned out across N hosts

The canonical use: N replicas boot and each tries to perform the same one-time migration, and exactly one must win.

1. Each host builds the *same* `ConditionalAppendRequest`: a stable `IdempotencyKey` that names the migration (e.g. `"migration:2026-07-add-region-tag"`) and the single migration-marker event (identical payload + tags on every host).
2. Each host calls `AppendIfUniqueAsync`. Exactly one gets `Appended`; all the others get `AlreadyCommittedSameOperation` carrying the winner's receipt. Both are success — a host that sees `AlreadyCommittedSameOperation` knows the migration is already durably claimed and proceeds as a no-op.
3. A host whose call returns a retryable error (in-doubt claim, transient store error) simply retries; the retry converges — once the winner commits, the retry classifies as `AlreadyCommittedSameOperation`.
4. If a host builds a *different* operation under the same key (different payload/tags), it gets `KeyReuseConflict` — a programming error surfaced loudly, not silently merged.

**One durable claim is the boundary.** The contract guarantees at most one durable claim per key; it does **not** make the migration's side effects exactly-once. If the migration itself performs external effects (writes to another system, sends notifications), gate those behind the winning claim through an outbox / idempotency layer — the claim tells you *who won*, not that the effect ran exactly once.

## PostgreSQL durable multi-tag expected-position CAS — SEK-G40

**Version: 10.19.0 (minor).** PostgreSQL now offers the optional `WriteConditionKind.ExpectedTagPosition`
capability. It is a durable DCB fence beneath Orleans reservations: it prevents a command that read stale consistency-tag
heads from appending after a partitioned/retired writer has bypassed the in-memory reservation layer. It is **not** a
replacement for reservations, and it does not make arbitrary external effects exactly-once.

### Additive contract and the three explicit states

`IEventStore` and every existing write/result shape remain unchanged. The opt-in `CommandExecutionOptions` has an
`ExpectedTagPositions` value, and the WASM boundary has a new V2
`VersionedExpectedTagPositionSerializedCommitRequest` plus optional
`ISerializedExpectedTagPositionSekibanDcbExecutor`. V1 and unversioned serialized payloads keep their old omission =
no-enforcement semantics byte-for-byte. WithResult returns typed errors in `ResultBox`; WithoutResult rethrows the same
typed exception through its guarded boundary.

Every *derived consistency tag* must have exactly one `TagHeadExpectationEntry(ServiceId, Tag, Expectation)`. The
expectation is an explicit discriminator — never a nullable-position convention:

```csharp
var noFence = TagHeadExpectation.NoEnforcement(); // still creates, locks, reconciles, and advances the durable head
var firstWrite = TagHeadExpectation.AssertEmpty(); // requires a transactionally proven-empty head
var continuation = TagHeadExpectation.Exact("01J...previous-position");
```

Missing, duplicate, unknown, malformed, or service-mismatched entries fail before reservation/store mutation.
`NoEnforcement` skips **only** comparison; it is useful when adopting the protocol while retaining unconditional command
semantics. An unsupported provider (InMemory, SQLite, Cosmos DB, DynamoDB) fails closed with
`ConditionNotSupportedException` before its handler/write method. `ConditionalAppend` and expected-tag positions are
separate protocols and cannot be combined in one options object; the ambiguous combination is rejected before a write.

### PostgreSQL all-writer protocol

The ordinary typed batch, serialized batch, and unique conditional-claim writer all reach one internal PostgreSQL
transaction seam. Thus even legacy/unconditional tagged writes participate in durable head maintenance while doing **no**
expected-head comparison. For the complete deduplicated `(ServiceId, Tag)` set the seam:

1. sorts ServiceId then Tag with ordinal comparison, lazy-inserts `dcb_tag_heads` in that order, and acquires
   `FOR UPDATE` in the same order;
2. bootstraps a newly inserted row from the service-scoped `MAX(dcb_tags.SortableUniqueId)` (a persisted null head is a
   *proven-empty* row, not an absent-row shortcut);
3. reconciles authoritative rows newer than the head, writing an append-only `dcb_tag_head_violations` record and
   repairing the head before any command DML;
4. compares **all** requested expectations and returns one `ExpectedTagPositionConflictException` containing the complete
   expected/observed pair set when any is stale; and
5. on success writes event/tag rows and advances each head to `max(reconciled head, the maximum position for *that tag*
   in the batch)`, so an unconditional older writer cannot regress a newer durable head.

Positions must strictly increase in an expected-position batch and each event must exceed every carried tag's reconciled
head. They are rejected rather than regenerated. If reconciliation found bypass evidence and comparison then fails, only
the repair and idempotent violation record commit; command event/tag rows and unrelated lazily-created head rows do not.
An ordinary mismatch with no repair rolls everything back, including lazy rows.

Violation records are service-scoped, indexed, append-only, and never auto-cleaned. Operators can read them with the
provisioning principal, for example:

```sql
SELECT "ServiceId", "Tag", "PreviousHeadPosition", "ObservedPosition", "DetectedAtUtc", "DetectingWriter"
FROM dcb_tag_head_violations
WHERE "ServiceId" = :service_id
ORDER BY "DetectedAtUtc" DESC;
```

The runtime does DML only — all three tables (`dcb_tag_heads`, `dcb_tag_head_violations`,
`dcb_tag_head_enablement_epochs`) are created by the PostgreSQL EF migration/provisioning plane. A runtime schema-missing
failure (for example SQLSTATE `42P01`) must be fixed by provisioning; it never triggers `CREATE`, `ALTER`, migrations, or
a catch-and-create fallback.

### Enablement epoch: an operational hard gate

The fence is only meaningful after every old PostgreSQL writer participates. Do this in this exact order:

1. provision the 10.19 schema/migration;
2. drain or quiesce **all** pre-10.19 PostgreSQL writers;
3. set one `dcb_tag_head_enablement_epochs` marker for the ServiceId with the provisioning principal;
4. enable `AssertEmpty` / `Exact` requests and begin monitoring violation records immediately.

Before the marker exists, an enforcement request fails with `TagHeadEnforcementNotEnabledException` before any store write
or head advance. The guarantee begins at the epoch; pre-epoch history is absorbed only by the authoritative bootstrap
maximum. Never set the marker merely because a quiet period had no violations: quiet tags have no reconciliation run.

### Honest reconciliation boundary

Reconciliation detects a bypass already visible when its `MAX` query runs. An old writer that inserts **after** that query
can escape this particular audit if a newer head later overtakes it. This is deliberately best-effort detection, not a
claim that premature mixed-version enablement is safe. The drain-before-epoch step is the correctness boundary; a zero
violation count is monitoring evidence, never clean-cutover proof.

<!-- sek-g44:cas-non-default -->
## Expected tag-position CAS is not a template default

`CommandExecutionOptions.ExpectedTagPositions` and its `AssertEmpty` / `Exact` policies are available for an
application that explicitly adopts the expected-tag-position protocol. They are **not enabled automatically** by a
DCB template: a deployment must provision its store, establish the service enablement epoch, and deliberately use
the conditional command/write APIs. A template must not seed `TagHeadEnablementEpochs`, run a migration at startup,
or add CAS write usage merely because the package graph contains the capability. Those are production consistency
decisions with a rollout and writer-drain requirement.

## SEK-G20 generation-aware checkpoint CAS

**Version: 10.8.0 (minor).** Closes the cross-cluster shared-store hole from SEK-G18 (see
[Common Issues → cross-cluster shared-store convergence](13_common_issues.md)). It is an **optional**
capability on the multi-projection checkpoint store (`IMultiProjectionStateStore`), feature-detected
from the LIVE instance exactly like the G15/G16 conditional-append discipline — a store advertises it
by implementing `IGenerationAwareCheckpointStore` and returning `CheckpointCapabilityKind.GenerationTombstoneCas`
from `DescribeCheckpointCapability()`. No member is added to `IMultiProjectionStateStore`, and no field
is added to the positional records (`MultiProjectionStateRecord` / `MultiProjectionStateWriteRequest`).

**Two-layer CAS.** Each checkpoint row carries a **generation** (rebuild epoch) and an **opaque
per-mutation token** — the exact-CAS revision (Postgres/SQLite: a `Revision` column; Cosmos: the
`_etag`; DynamoDB: a `revision` attribute) — plus a **lifecycle** (Active / Tombstoned). Every
conditional operation compares the EXACT token; a generation-only comparison is not a CAS. The fixed
state machine is `Active(g,rev) → CAS Invalidate → Tombstoned(g+1,rev') → CAS CommitRebuilt →
Active(g+1,new rev)`; the rebuilt payload commit and the tombstone clear are ONE atomic same-row CAS.

**What it protects.** A retrograde full-rebuild replaces delete-based invalidation with a durable
bump+tombstone; a stale peer's later persist is `ConditionRejected` and never re-contaminates the row;
a fresh activation reads the control plane before binding any payload, so a tombstone forces a full
ordered replay. Every product checkpoint mutation routes through the surface (no unconditional
write/delete bypass).

| Provider | Exact-token primitive | Schema upgrade |
|----------|----------------------|----------------|
| Postgres | conditional `UPDATE … WHERE Generation=@g AND Revision=@r AND Lifecycle=@l` (row-count) | additive EF migration (`Generation`/`Revision`/`Lifecycle`, default 0) |
| SQLite | conditional `UPDATE` (row-count) | additive `ALTER TABLE … ADD COLUMN … DEFAULT 0` |
| Cosmos | `ReplaceItem` with `IfMatchEtag` (412 → rejected) | `generation`/`lifecycle` item properties (absent → 0) |
| DynamoDB | conditional `PutItem`/`UpdateItem` `ConditionExpression` | `generation`/`revision`/`lifecycle` attributes (absent → 0) |

**Compatibility.** Existing rows read as **generation 0, revision 0, Active** — no event or payload
migration; only the additive checkpoint-store schema upgrade is required and is proven against a real
pre-G20 database per provider. Until the schema is applied, capability operations fail closed (no
silent legacy fallback). A store that does **not** implement the capability keeps its legacy
unconditional write byte-for-byte, and a retrograde invalidation against it **fails closed to the G14
fault path** (operator reset required) rather than risking a cross-cluster stale re-contamination.

**Mixed-version hazards (must be documented per deployment).** Protection is complete only when every
WRITER and READER is upgraded. On **SQLite**, the legacy `INSERT OR REPLACE` upsert deletes+reinserts
the row and therefore RESETS the control columns — a pre-G20 writer erases a tombstone. Roll all
clusters/writers to 10.8.0 before relying on the cross-cluster guarantee.

## Related

For the current internal-use cold event export, hybrid read, and catch-up worker setup, see [Cold Events and Catch-up](19_cold_events.md).
