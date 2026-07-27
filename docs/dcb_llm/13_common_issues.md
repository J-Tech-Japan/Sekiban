# Common Issues and Solutions - DCB

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
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md) (You are here)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Failed to Reserve Tags

**Symptoms**: `InvalidOperationException` with message "Failed to reserve tags: ..."

**Causes**:
- Optimistic concurrency mismatch (someone else wrote newer events).
- Tag already reserved and not yet confirmed (long-running command, actor restart).

**Fixes**:
- Include `ConsistencyTag.FromTagWithSortableUniqueId` when retrying after a read to guarantee correct version.
- Inspect `TagConsistentActorOptions.CancellationWindowSeconds` to adjust hold timeout.
- Monitor reservations via custom telemetry around `MakeReservationAsync`.

## Missing Type Registration

**Symptoms**: `InvalidOperationException` about unknown event/tag/projector.

**Fixes**: Ensure the type is registered in `DomainType.GetDomainTypes()` (`internalUsages/Dcb.Domain/DomainType.cs`).
Remember to register query types when introducing new projections.

## Projection Not Updating

**Symptoms**: API returns stale data, `WaitForSortableUniqueId` times out.

**Causes**:
- MultiProjection grain fell behind due to stream disconnection.
- Projection version mismatch (bumped in code but not deployed).

**Fixes**:
- Check Orleans dashboard for grain exceptions.
- Restart the silo to force catch-up.
- Verify that projection version strings match what is registered in domain types.

## Postgres Errors on Startup

**Symptoms**: Migration failures or missing tables.

**Fixes**:
- Run `dotnet run --project Sekiban.Dcb.Postgres.MigrationHost` before starting the silo.
- Ensure connection string points to a database where the user has create/alter permissions.
- Confirm `Sekiban:Database` configuration is set to `postgres`.

## Cosmos Throughput Limits

**Symptoms**: `429` (Request rate too large) exceptions.

**Fixes**:
- Increase RU/s on the `events` and `tags` containers.
- Use `WaitForSortableUniqueId` sparingly in read-heavy scenarios to reduce polling frequency.

## Cosmos: an event is visible globally but missing from tag-scoped reads

**Symptoms**: `ReadAllEventsAsync` returns the event, but `ReadEventsByTagAsync` does not. `TagExistsAsync` says false, tag projectors never see it, and `GetLatestTagAsync` — which feeds `GeneralTagConsistentActor`'s optimistic-concurrency baseline — reports a value older than the event you just wrote.

This is the symptom [issue #1046](https://github.com/J-Tech-Japan/Sekiban/issues/1046) reported. Only Cosmos does this; Postgres writes events and tag rows in one transaction and cannot.

### First: is it lag, or is it residue?

They look identical for a few seconds and need completely different responses.

- **Transient lag** — the write is still in flight, or the account's consistency level is letting a reader see a stale replica. **Wait and re-read.** If the tag read catches up on its own, nothing is wrong.
- **Durable residue** — the tag rows never landed. The Cosmos write is two-phase (events first, then tag rows), so a crash between the phases leaves the event durable with no tag rows, and **no amount of waiting fixes it.**

If the event is minutes old and still invisible to tag reads, it is residue.

### Then: check the telemetry

The `Sekiban.Dcb.CosmosDb` meter says whether a write actually failed:

- `sekiban.dcb.cosmos.tag_write.failures` / `.retry_outcomes{outcome=exhausted}` — a tag write gave up. The structured logs and `CosmosTagWriteExhaustedException` name the affected event ids.
- `sekiban.dcb.cosmos.event_write.partial_failures` — a multi-event write landed some events and not others.

A crash leaves no in-process trace at all, so **an empty metric does not mean an empty tags container.** Confirm with a dry run.

### Fix: repair the tag index

The tags container is a **derivable index** — every event document carries its complete `tags` array — so missing rows can be rebuilt from the events. Register the repair service and **always dry-run first**:

```csharp
services.AddSekibanDcbCosmosDb(configuration);
services.AddSekibanDcbCosmosDbTagRepair();   // opt-in; not registered by AddSekibanDcbCosmosDb alone

var repair = await factory.CreateAsync(serviceId);

// Look before you write. DryRun is the default.
var report = await repair.RepairAsync(new CosmosTagRepairOptions
{
    DryRun = true,
    ToSortableUniqueIdInclusive = lastSettledSortableUniqueId,  // pin the upper bound
});
```

**Pin `ToSortableUniqueIdInclusive`** to an event you know is settled. Without it the scan runs to the end and races your live traffic, which the write path is already handling — the repair is for crash residue, not for events that are still being written.

Read the report, then repair with `DryRun = false`. The counts tell you what you are looking at:

| Category | Meaning | Does repair write? |
|---|---|---|
| `Missing` | Nothing indexes this `(event, tag)`. | **Yes — the only category it writes.** |
| `Present` | The derived row exists and matches the event. | No |
| `LegacyPresent` | A row from before the deterministic-id scheme indexes it. It works; migration is optional. | No |
| `Duplicate` | Several legacy rows index it. | No — reported only |
| `Corrupt` | A row indexes it but disagrees with the event. | **No — never overwritten.** Investigate before doing anything. |
| `Overflow` | More rows than the per-key cap. | No — raise `MaxRowsPerKey` to look deeper |

A `Corrupt` or `Duplicate` count is not something the repair will resolve for you, by design.

### Prevent the recurrence you *can* prevent

Set `WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward`:

```csharp
services.AddSekibanDcbCosmosDb(
    configuration,
    options => options.WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward);
```

The default is `Compatible`, which does not retry the tag write and — via the now-obsolete `TryRollbackOnFailure` — **deletes the events it already wrote**, which multi-projections may already have read. `RollForward` retries the tag write instead and never deletes an event.

**The caveat that matters**: `RollForward` only helps when the process survives to retry. **A crash is not an in-process failure**, so it leaves residue no policy can prevent — only a repair pass closes that window. An [opt-in sweep](11_storage_providers.md) can run the repair automatically over a recent window, but it is *eventual* repair: it does not gate tag readers, and the window stays open until a run reaches it.

If your workload cannot tolerate that window at all — money-sensitive workflows above all — **use the Postgres provider**, which has none of these gaps.

**See**: [Consistency Contract](11_storage_providers.md#consistency-contract) for the full per-provider guarantees, the repair service, the sweep, and what none of them promise.

## WithoutResult: a failure that arrived as a bare `NullReferenceException`, or as no failure at all

**Symptoms**: calling a `Sekiban.Dcb.WithoutResult` API — `ISekibanExecutor`, `ICommandContext`, the Orleans executor — and getting a `NullReferenceException` with no message and nothing in it that names the call you made. Or, worse, getting no exception at all and a plainly wrong answer: `TagExistsAsync` reporting `false` for a tag that exists, a value-typed query returning `0`.

**Where this came from**: [issue #1045](https://github.com/J-Tech-Japan/Sekiban/issues/1045) was reported as a `NullReferenceException` from `CosmosDbEventStore.WriteEventsAsync`, and it turned out to have **two separate causes, only one of which was ours**:

- **The reporter's own cause — stale state, not a Sekiban defect.** Their staging environment was redeployed frequently without event/projector-versioning discipline, so stale projection state accumulated until reads began returning null. Recreating the affected stores cleared it, and their production environment (which does version properly) never hit it. If you see this, check your projector versioning before anything else.
- **Sekiban's cause — a diagnostics defect.** That null surfaced through `UnwrapBox()` as a bare `NullReferenceException` with no message, so an environment problem looked exactly like a library bug and took far longer to find than it should have. That is the half we fixed.

**And a third thing, found while fixing it, which nobody had reported**: the same `UnwrapBox()` was *silently swallowing* failures at value-typed boundaries. That is a correctness defect of our own, unrelated to the reporter's stale state, and it is described below.

**Cause**: the WithoutResult packages are a facade over the `ResultBox`-native core: internally every operation returns a `ResultBox<T>`, and at the edge the box is opened. Opening it used to be `UnwrapBox()`, whose behaviour depends on the shape of `T`:

| shape of the box | what the caller used to get |
|---|---|
| failed, `T` is a reference type (e.g. `TagState`) | the carried exception, rethrown — correct |
| failed, `T` is a **value type** (e.g. `bool`, `int`) | `default` — **the failure was silently swallowed** |
| no box at all (an internal path returned `null`) | `NullReferenceException`, no message, no operation name |

Row 3 is what issue #1045 hit. Row 2 is the one nobody hit yet, and it is worse, because it changed answers rather than just messages. `ICommandContext.TagExistsAsync` returns `bool`, so an event store that could not be reached came back as `false` — indistinguishable from "no, that tag does not exist" — and a command handler guarding on it would go on to create the entity it had just been told, wrongly, was absent.

**Fixed in 10.3.0**. Every WithoutResult boundary now opens the box under one policy:

- **A failure the box carries is rethrown as itself** — same exception, same type, same stack — whether `T` is a reference type or a value type. Your `catch (SekibanValidationException)` and `catch (OperationCanceledException)` keep working exactly as before; cancellation still carries its original `CancellationToken`. What is new is that value-typed boundaries throw it instead of swallowing it.
- **The boundary is recorded on the exception**, not wrapped around it: `ex.Data["Sekiban.Boundary.Operation"]` (e.g. `ICommandContext.TagExistsAsync`) and `ex.Data["Sekiban.Boundary.Target"]` (e.g. the tag, the command type). The exception type is untouched.
- **When there is no failure to rethrow**, you get a `SekibanBoundaryException` (namespace `Sekiban.Dcb.Boundaries`) naming the operation, instead of a bare `NullReferenceException`. This means an internal path returned something it should not have — please report it, with the message, which now tells us where to look.

**What to do if you see `SekibanBoundaryException`**: it is not a domain error and there is nothing to retry. It says a Sekiban internal returned no result. Open an issue with the message — `Operation` and `Target` are on the exception.

## The host refuses to start: "Production requires a distributed runtime"

**Symptoms**: `SekibanDcbProductionGuardException` at startup after opting into `AddSekibanDcbProductionGuard()`.

**This is the guard working.** It resolved your executor and asked what it is, and the answer was not a distributed
runtime — you have an in-process (test) executor, or one that will not say what it is, registered in an environment the
guard treats as Production. In-process actors mean no cluster coordination: two hosts do not see each other's tag
reservations, so the invariants DCB exists to enforce are not enforced. There is no override for it.

**Fix**: use a distributed-runtime executor. If this is a local environment that is *named* Production, either rename
it, or use a single-silo localhost Orleans host — which is a real distributed runtime and needs nothing installed. See
[Localhost Orleans](22_localhost_orleans.md).

## The startup banner says my storage is Volatile

That is data loss, everywhere except a test. See [Storage providers](11_storage_providers.md#durability-descriptors-and-the-production-guard).

## An event's values come back null even though the row clearly has them (#1074)

**Symptoms**: a query returns empty or a projection looks unpopulated; you inspect the stored event and its JSON plainly holds values, but the deserialized payload's properties are all null / 0 / default. No exception was thrown when the payload was read.

**Cause**: the stored payload's property names do not match the casing the reader binds with. Sekiban writes and reads camelCase, case-sensitively. A payload written in PascalCase — e.g. by a producer that serialized with a bare `JsonSerializer.Serialize(x)` instead of the domain's options — has member names (`StudentId`) that do not bind to the declared members (`studentId`). System.Text.Json, binding case-sensitively, simply leaves each unmatched member at its default and reports success. A producer-side data bug becomes an all-null instance on the reader side, with nothing pointing at the cause.

**Fixed in 10.4.0** (SEK-G13): by default the reader now **fails loud** on this exact shape. A top-level member that does not bind AND matches a declared name except for casing throws `SekibanEventPayloadBindingException`, naming the event type, the CLR type, the offending JSON name, the expected name, and the payload location — never a payload value. Genuinely unknown members (an additive field from a newer writer) are still ignored, so forward compatibility is unaffected, and correct camelCase rows are never touched. The check is top-level only, by contract — it does not recurse into nested objects.

**What to do**:

1. **Fix the producer.** Serialize through the domain's options (camelCase), not a bare `JsonSerializer.Serialize`. That is the real fix; the exception is pointing at a real data bug.
2. **To read existing mis-cased rows while you migrate**, choose a deserialization policy when you build the domain types:

   ```csharp
   var domainTypes = DcbDomainTypesExtensions.Simple(
       configure,
       deserializationPolicy: EventPayloadDeserializationPolicy.CaseInsensitiveLegacy);
   ```

   `CaseInsensitiveLegacy` binds a top-level member to its declared counterpart regardless of casing. It is a migration aid, not a fix: it does not rewrite your stored data and does nothing for nested casing.

### The four policies

| policy | top-level case mismatch | unknown field | when |
|---|---|---|---|
| `FailOnCaseMismatch` (default) | throws | ignored | new systems; catches #1074 |
| `CompatibleCaseSensitive` | binds to null (pre-G13) | ignored | temporary escape hatch while migrating off the old silence |
| `StrictUnmapped` | throws | throws | when any non-exact payload must be rejected |
| `CaseInsensitiveLegacy` | binds (top-level) | ignored | reading legacy mis-cased rows during migration |

For identifiers and other members that must be present, declare them `required` (C#) or `[JsonRequired]` — a missing required member then fails through the same descriptive exception, which is the only safe way to enforce presence (blanket null-checking would reject legitimately-default values).

## A query returns empty, but the events are there and durable (#1075)

**Symptoms**: a list query returns zero rows, or a projection reads as unpopulated, yet the events plainly exist in the store. No exception, no error — just "no data". Often paired with #1074: a payload that cannot be folded produces exactly this silence.

**Cause**: a projection could not apply one of its events — a fold that threw, or a payload that would not deserialize — and the failure was swallowed. The projection stopped advancing at the poison event and presented whatever it had built so far (often nothing) as a *successful* empty result. "Durable events exist but cannot be projected" was masked as "there is no data", which is the most expensive kind of failure to notice.

**Fixed in 10.4.0** (SEK-G14). A projection that cannot fold an event now **faults**, and a fault fails queries with context instead of answering empty:

- The in-memory replay no longer swallows: a failed read or a failed fold surfaces as an error from the executor's normal `ResultBox`/exception boundary.
- An Orleans multi-projection records a **confirmed fault** — event id, type, projector, position — and every query surface (state, scalar, list) fails with that context while the fault is unresolved. The fault is persisted, so a freshly-activated grain re-establishes it before answering the first query rather than briefly reporting empty.
- **Ordinary catch-up lag is not a fault.** A projection that is simply behind still answers queries; only a projection that has *crashed* on an event fails them.

### The fault-vs-lag contract

| situation | query behavior |
|---|---|
| healthy, caught up | succeeds |
| healthy, still catching up (lag) | succeeds (partial/current state) |
| **faulted** (an event could not be folded) | **fails with fault context** — event id, type, projector, position; never a payload value |

**What to do when a query fails with a projection fault**:

1. The message names the event, the projector and the position. Find that event.
2. If it is a casing/deserialization problem, it is #1074 — fix the producer, or read the row with the `CaseInsensitiveLegacy` deserialization policy while you migrate.
3. If the projector's fold logic is at fault, fix the projector.
4. Once the cause is resolved, **rebuild the projection** with the operator reset below. A fault clears only by a deterministic rebuild that successfully replays the same position — an unrelated later event never clears it. If the poison is still there, the rebuild simply faults again at the same event.

Poison-event skip/quarantine is deliberately **not** a default: a projection silently skipping events it cannot apply is a return to the same class of silence. It may arrive later as an explicit, opt-in policy.

### Operator reset: `ResetProjectionFaultAsync` (admin-plane, operator-only)

Clearing a persisted projection fault is an **operator-only** action on the grain admin interface (`IMultiProjectionGrain`, alongside `GetStatusAsync`). It is **never invoked automatically** and is **not** exposed through `ISekibanExecutor` — application/query code cannot trigger it.

- **Acquire the token first.** The reset requires the *exact* current fault identity — projector name, fault event id, and fault stream position — as a concurrency token. Read it from the fault context surfaced on a failed query (the projection-fault error carries event id, type, projector and position). Do not synthesise it.
- **The persisted descriptor is the authority.** The token is compared against the current *persisted* fault inside the single-writer gate. A stale token, a descriptor changed by a concurrent write, a wrong projector, or no fault present is rejected with a normal error and **no write and no fault clear**. A same-token race commits at most once.
- **Derived state is rebuilt.** A correct token durably clears the descriptor **and** the derived projection checkpoint, so the projection **rebuilds from the beginning** by catch-up. This does not delete any authoritative events — only the grain's derived snapshot/checkpoint. The first-query barrier prevents any early "healthy" answer before the rebuild reaches the head.
- **The clear is earned, not assumed.** Only after the durable clear commits is the live actor fault cleared and a fresh activation requested. If the poison still cannot be folded, the per-event boundary **re-establishes and re-persists** the fault on rebuild — the reset never skips or quarantines. A permanent clear happens only when the same position replays successfully.
- **Partial-failure semantics (two stores).** The reset touches two derived stores — the grain state (the descriptor) and the external snapshot store — in this order, under the single-writer gate: validate the token, then invalidate the external snapshot, then durably clear the descriptor. It is **not** a single atomic transaction, so:
  - If the **external invalidation fails**, the descriptor clear is skipped: descriptor, live fault and the external snapshot are all retained — nothing changed. Retry the same token once the store recovers.
  - If the external invalidation **succeeds but the descriptor clear fails**, the external snapshot is already gone but the descriptor and live fault are retained, so every query stays rejected (fail-closed). This is coherent: a later rebuild regenerates the snapshot, and the retained descriptor keeps the projection rejecting queries until you retry the same token, which then completes the clear. A deleted-but-not-yet-regenerated snapshot is harmless — it is derived-only, and no authoritative events are ever deleted.
  - No **in-flight upsert can race the delete**: **every** external snapshot mutation goes through one activation-local coordinator — all three upsert paths (normal persist, streaming persist, version rewrite) **and every delete**, including the public `DeleteExternalStateAsync` admin call and the reset's own invalidation. No direct-to-store bypass remains. So the delete waits for any parked/in-flight upsert to finish before it deletes, and no upsert runs concurrently with it. On top of that, the coordinator **rejects any upsert while a live or committed fault exists**, so a faulted projection persists no derived state and no stale upsert can recreate what the reset removes. Catch-up runs on an interleaving grain timer, so this coordinator — not just non-reentrancy — is what makes the ordering hold. (A durable epoch/tombstone would only be needed for a hypothetical multi-writer or cross-silo persister, which the current model does not have.)
  - The fault rejection is an **explicit failure, not a silent success**: a blocked upsert returns a `ResultBox.Error` carrying a stable `ExternalPersistenceBlockedByFaultException`, never a success carrying `false`. This matters because callers that inspect only `IsSuccess` would otherwise mistake a rejection for a completed save — so normal and streaming persistence never report "saved" or advance any persisted metadata after a rejection, and a version rewrite keeps `updated = false` and performs no projector-version write.

## Conditional (unique-key) append: in-doubt, key-reuse, and the Cosmos tag-repair gate

This concerns the optional SEK-G15/G16 `IConditionalEventStore.AppendIfUniqueAsync` contract (see [Storage Providers](11_storage_providers.md)). It is not on the unconditional write path.

**Symptoms & what each outcome means**:
- `ResultBox.Error` carrying `ConditionalAppendInDoubtException` (WithoutResult: the same exception is thrown). The append could **not** be resolved to a definite outcome. This is **retryable** (`IsRetryable == true`) — it is NOT a fifth success/conflict status.
- `KeyReuseConflictException`. The idempotency key is already claimed by a **different** operation (the persisted fingerprint differs). This is a programming error surfaced loudly, not a transient.
- `DynamoConditionalAppendLimitException`. The request would exceed a DynamoDB `TransactWriteItems` limit (more than 100 items = 1 event + 100 tags, or a duplicate item key from duplicate tag strings). Raised **before any network call**; permanent, not retryable.
- `ConditionalAppendCommittedStateCorruptionException`. A same-operation retry found an existing committed index/tag row that **disagrees** with the event. This is **NOT retryable** (`IsRetryable == false`) — the row is never overwritten. Investigate; do not loop retrying.
- `ConditionNotSupportedException`. The resolved store cannot enforce the requested write condition — fails closed before any store call rather than degrading to an unconditional write.

**Causes & fixes**:
- **In-doubt after a conflict with no readable winner, or after a cancellation/timeout** (`ReasonCode` `winner-unreadable-after-conflict` / `ambiguous-after-write`): the store signalled a claim collision (or the call was cancelled after a possible durable commit) but no committed winner could be read back to verify it. **Retry** — the retry converges to `AlreadyCommittedSameOperation` once the winner commits. A post-durable-commit cancellation/timeout is first resolved by an authoritative read + fingerprint (and, on Cosmos, the committed-state gate) to `AlreadyCommittedSameOperation` when possible, and only otherwise surfaced as in-doubt.
- **In-doubt because the committed state could not be verified** (`ReasonCode` `committed-state-unverified`): this is **Cosmos-specific**. Cosmos writes the event document and its tag rows in separate phases, so a crash after the event create leaves a committed event whose tag-scoped visibility is not yet restored. A same-operation retry idempotently repairs every **missing** tag row **before** returning `AlreadyCommittedSameOperation`; if that repair still fails transiently (the tag store is still faulting) the outcome is in-doubt — never a false `AlreadyCommitted` while tag rows are missing. **Fix**: retry; if it persists, run the Cosmos tag repair/sweep (see [Storage Providers](11_storage_providers.md)).
- **Non-retryable committed-state corruption** (`ConditionalAppendCommittedStateCorruptionException`): a same-operation retry found a tag row that already exists under the deterministic identity but **disagrees** with the event (strict content mismatch). Unlike a missing row, a disagreeing row is an integrity violation: it is never overwritten and retrying will not clear it. **Fix**: investigate how a conflicting row came to occupy that identity (an out-of-band writer, a botched migration); do not loop retrying.
- **A key-reuse conflict you did not expect**: two different payloads/tags were submitted under the same idempotency key. Keys must name the *operation*, not the caller. Use a stable key derived from the operation identity (e.g. the migration name), and build the identical event on every host.
- **A DynamoDB limit error**: reduce the tag count to ≤ 99 (1 event + 99 tags = the 100-item cap) and deduplicate tag strings; a conditional append is single-event by contract.
- **Secret-safety**: none of these exceptions carry the raw idempotency key or the payload — only opaque fingerprints, the provider name, the ServiceId, and the *derived* EventId (and, for corruption, a derived row-id hash). Do not add logging that reconstructs the key from the request.

### Post-write failure taxonomy (what to do on each)

A failure's timing relative to the durable commit decides the recipe. These are distinct — do not treat every post-write error as retryable or as success:

- **(a) Rollback before commit (known pre-commit failure)** — the write failed (or was cancelled) BEFORE it committed; no durable claim exists. It surfaces as the EXACT original provider/cancellation/transport exception (the original `OperationCanceledException` and its `CancellationToken` are preserved) — NEVER a typed `AmbiguousAfterWrite`, which is reserved for provider-declared post-commit ambiguity. **Recipe**: retry; it converges by claiming the key. *Proven by*: SQLite and Postgres "cancelled-before-commit" tests (real DB) — nothing durable, original cancellation surfaced, retry appends.
- **(b) Response loss after a durable commit (transport OR cancellation)** — the transaction/event+rows committed durably, then the response was lost. The provider signals an internal post-commit ambiguity marker (never the raw transport exception), and the shared orchestrator resolves it authoritatively **on the same call** under a BOUNDED, caller-independent verification budget — an authoritative winner read + fingerprint (and, on Cosmos, the committed-state gate). It returns `AlreadyCommittedSameOperation` with the original winner's receipt. **Recipe**: usually nothing — the same call already returned the winner's receipt. *Proven by*: Postgres (real DB), SQLite (real DB), Cosmos multi-tag (production fault seam over an in-memory double) post-commit tests, all asserting same-call AlreadyCommitted with the exact stored-winner receipt.
- **(c) Bounded verification cannot complete** — the same-call authoritative verification exceeds its independent budget (a hung/failed provider) or finds no readable winner. Surfaces as typed retryable `ConditionalAppendInDoubtException` (`AmbiguousAfterWrite`) preserving the original cause and its exact `CancellationToken`; the caller's own cancellation never cancels the verification. **Recipe**: retry; it converges once the winner is readable/committed. *Proven by*: a bounded-verification unit test over the shared orchestrator (readback within budget → AlreadyCommitted; readback hangs past budget → prompt typed `AmbiguousAfterWrite`).
- **(d) Conflict with unresolvable winner** — a claim conflict could not be resolved because the winner could not be read (or its committed state could not be verified). Surfaces as typed retryable `ConditionalAppendInDoubtException` with a closed `Reason`. **Recipe**: retry; it converges once the winner is readable/committed. *Proven by*: Cosmos bare-409-no-winner and repair-failure tests; DynamoDB bare-conflict-no-winner test.

Provider-test fidelity: Postgres uses a real container (Testcontainers) and SQLite a real temp-file database; Cosmos uses the production fault seams over an in-memory faithful double; DynamoDB uses a thread-safe fake. Claims of "real provider" apply only to Postgres/SQLite.

## Serialization Exceptions

**Symptoms**: `JsonException` during event replay or API responses.

**Fixes**:
- Keep event payloads backward compatible; avoid removing required properties.
- When renaming records, register both old and new names via custom converters.
- Validate that `[GenerateSerializer]` attributes exist for Orleans-managed payloads.

## Azure Queue Stream Issues

**Symptoms**: Missing events in projections when using Azure Queue streams.

**Fixes**:
- Ensure queues exist and the service principal has permissions.
- Adjust `BatchContainerBatchSize` and `GetQueueMsgsTimerPeriod` for throughput vs latency trade-offs.
- If running locally, verify Azurite connection strings.

## Dapr Integration

Not yet available. If you see references to `Sekiban.Pure.Dapr`, they apply to the pure aggregate runtime, not DCB.
Stick with Orleans until Dapr support ships.

## Serialized-Commit Wire Contract Compatibility (SEK-G17)

**Symptoms**: A mixed-version deployment reports the serialized-commit wire contract as "broken", or an endpoint returns a
null-reference 500 on a request that looks structurally off.

**Causes**:
- The official contract (`eventCandidates` + base64 `payload` + `eventPayloadName` + **per-event `tags`** +
  `consistencyTags`) has NOT changed across dcb-v10.2.2 → 10.6.0. Issues #1087/#1088 traced the reported break to a
  DIFFERENT downstream shape (`events` / `payloadJson` / per-commit-`tags`) exposed under the same route and misdescribed as
  mirroring the framework contract.
- The framework contract historically had no version discriminator, no pinned property names, no normative spec, and no
  golden-wire tests, so a wrong compatibility claim had nothing to fail against.

**Fixes**:
- Treat the normative spec in `07_json_orleans_serialization.md` ("Serialized Commit Wire Contract") as authoritative, and
  make any compatibility claim pass the golden vectors (`SerializedCommitWireGoldenTests`).
- Do NOT add serialization attributes to the positional DTOs to "fix" naming; pin via the contract-owned
  `SerializedCommitWireContract` / `SerializedCommitWireJsonContext` instead. Attributes would change fresh-options
  consumers' PascalCase output.
- For an explicit version and typed errors, accept requests through `ISerializedCommitAcceptor` /
  `SerializedCommitAcceptor`: an unknown `version` fails closed with `UnsupportedSerializedCommitEnvelopeVersionException`
  and a malformed shape with `MalformedSerializedCommitException` — before any side effect, instead of a null-reference 500.
- A downstream adapter that collapses per-event tags into per-commit tags may do so ONLY when every event in the commit
  shares an identical tag set, and must reject the rest explicitly.

## Multi-Projection Cross-Instance Divergence / Checkpoint Drift (SEK-G18)

**Symptoms**: In a scale-out topology (2+ instances over one shared event store), two
instances answer the SAME query with DIFFERENT values for a racing-create entity and never
converge, yet report `IsSafeState=true`. Or, across an independent-host restart with zero new
events, the persisted checkpoint's position/`EventsProcessed` changes.

**Causes**:
- The served (unsafe) multi-projection state used to fold events in ARRIVAL order and was
  never reconciled against the globally-ordered safe state (#1092), so first-arrival won
  permanently and `IsSafeState` was attached from a timestamp comparison (a lie for an
  unreconciled payload).
- On checkpoint restore, catch-up start was re-inferred rather than taken from the record's
  `LastSortableUniqueId`, re-folding an already-reflected event (#1086).

**Fixes** (framework, SEK-G18 — no action required beyond upgrading):
- The served state is now re-derived at every graduation as `safe + ordered remaining buffer`;
  `IsSafeState=true` means served-identical-to-safe. Out-of-global-order safe promotions on a
  compacted baseline trigger a full ordered rebuild from the authoritative store; queries await
  the rebuild barrier or fail closed (never a stale success).
- Catch-up after restore starts exclusive of the record's `LastSortableUniqueId`.
- Application projectors should still implement create arms as true first-event-wins
  (`if (state.Contains(id)) return state;`) so the globally-earliest event is authoritative.
