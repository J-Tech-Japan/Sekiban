# Durable Orleans Subscriptions

`Sekiban.Dcb.Postgres` provides an opt-in, PostgreSQL-backed subscriber for applications that need a durable cursor
after an Orleans process restart. The Orleans stream is only a data-free wake-up hint; the PostgreSQL event store is
the source of truth and the durable runner reads events strictly after its acknowledged position.

## Registration

Register the normal PostgreSQL event store, then add one durable subscription per service/name identity:

```csharp
builder.Services.AddSekibanDcbPostgres(connectionString);
builder.Services.AddSekibanDcbPostgresDurableSubscription(
    options =>
    {
        options.ServiceId = "orders";
        options.Name = "billing";
        options.StartPolicy = DurableSubscriptionStartPolicy.FromBeginning;
        options.ProvisioningMode = DurableSubscriptionProvisioningMode.PreProvisioned;
    },
    async (eventRecord, cancellationToken) =>
    {
        await HandleEventAsync(eventRecord, cancellationToken);
    });
```

`FromNow` is the default and starts at the current safe tail; `FromBeginning` starts with a null cursor. Names are
unique within a service container and duplicate registrations are rejected before the hosted runner is added. The
legacy `IEventSubscription` callback path is unchanged and is not converted into a durable subscription. A durable
subscription name is not reserved against legacy callbacks because the two paths have no shared declaration registry,
so the library cannot detect or reject an overlapping name. If an application registers both for the same logical
subscription, application handling may be invoked twice independently and handlers must already be idempotent; only
the durable runner reads and advances the durable cursor, and the legacy callback cannot move it.

## PostgreSQL deployment

The owner must provision the raw table before using pre-provisioned mode:

```csharp
await serviceProvider.ProvisionDurableSubscriptionSchemaAsync(cancellationToken);
```

`PreProvisioned` runtime operations are DML-only. The durable row stores the service/name identity, initialization and
acknowledged cursor, phase, failure and halt evidence, owner/generation, lease expiry, and retry timing. Missing table
or schema columns are surfaced as the provider's PostgreSQL error (for example SQLSTATE `42P01` for an absent table or
`42703` for an absent column); the runner does not silently treat an absent state row as healthy.

The default `LegacyAutoProvision` mode preserves compatibility for applications that allow startup provisioning. Use a
single owner-controlled provisioning step in deployments where the runtime database principal must not execute DDL.

## Recovery and fencing

Each catch-up cycle reads the durable row before attempting a PostgreSQL lease acquisition. Lease acquisition,
renewal, acknowledgement, failure recording, resume, and halt are service/name/owner/generation scoped. A successful
handler invocation is acknowledged only after event conversion and handling complete. A conversion error halts the
subscription immediately; handler failures retry up to `MaxHandlerAttempts` (four by default), then persist a halted
phase and the failure position/reason. `ResumeAsync` is explicit, preserves the acknowledged cursor and halt evidence,
clears retry/lease state, and advances the owner generation.

The runner renews a lease during a long handler and cancels the handler if renewal loses the fence. A duplicate or
out-of-order Orleans nudge cannot advance the cursor, and a process restart resumes from the PostgreSQL row. This is a
durable delivery/catch-up boundary, not an exactly-once guarantee for external side effects; handlers should remain
idempotent.

Lease ownership, expiry, retry eligibility, and halt fencing are evaluated by PostgreSQL time, not by a client clock.
The persisted `DurableSubscriptionPhase` is deliberately separate from the process-local
`DurableSubscriptionRunnerStatus` exposed by the handle (`Starting`, `Owner`, `Standby`, `Halted`, or `Stopped`). A
same-owner nudge or fallback poll retains the lease and generation, and an empty/idle observation does not release it;
the runner can therefore catch up promptly without waiting for lease expiry or manufacturing a new generation. Retry
attempts are admitted only when the durable `next_attempt_at_utc` is due in PostgreSQL, including after restart or
takeover. A stale owner cannot overwrite the first durable halt position, reason, or timestamp. Registration also fails
closed when `PreProvisioned` and `LegacyAutoProvision` are mixed, regardless of registration order; no partial provider
registration is left to silently change the provisioning mode.

The Orleans integration follows the DCB-supported `Microsoft.Orleans.*` **10.3.1** line. Upgrade the whole cluster
together; mixing Orleans versions is not a supported compatibility guarantee. This subscriber is the durable
store-driven catch-up boundary only. It does not provide a relay/outbox, replay arbitrary external subscriber effects,
or perform automatic historical repair; those concerns remain a separate later design.

## Limits and observability

`SafeWindow`, `LeaseDuration`, `PollInterval`, `RetryDelay`, `MaxHandlerAttempts`, and `MaxBatchSize` are validated
before the hosted runner starts. `GetStateAsync`, `ResumeAsync`, and `HaltAsync` expose the persisted phase and halt
evidence through `IDurableSubscriptionHandle`. Orleans subscription delivery does not provide the authoritative event
payload to the handler.
