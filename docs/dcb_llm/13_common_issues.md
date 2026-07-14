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
