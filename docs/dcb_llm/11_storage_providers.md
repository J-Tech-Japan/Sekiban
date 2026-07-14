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

## Materialized View Storage

Materialized views are currently implemented for PostgreSQL:

- `Sekiban.Dcb.MaterializedView` – core contracts and hosted catch-up worker
- `Sekiban.Dcb.MaterializedView.Postgres` – registry, executor, row access, and table updates
- `Sekiban.Dcb.MaterializedView.Orleans` – grain orchestration and query accessor

This is separate from the main event store package. In the current PoC, a service can store events in one database and
materialized view tables in another PostgreSQL database or schema. See [Materialized View Basics](20_materialized_view.md).

## Related

For the current internal-use cold event export, hybrid read, and catch-up worker setup, see [Cold Events and Catch-up](19_cold_events.md).
