# Unsafe Window Materialized View (v1)

> **Navigation**
> - [Materialized View Basics](20_materialized_view.md)
> - [Core Concepts](01_core_concepts.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)

Unsafe Window Materialized View is the read-side framework that applies safe/unsafe separation to a PostgreSQL-backed read model. It sits alongside the classic free-SQL materialized view described in [20_materialized_view.md](20_materialized_view.md): classic MV is still the right choice when a projector does not need ordering guarantees, and Unsafe Window MV is the right choice when you want late-arriving events to correct the read model without writing manual reconciliation code.

This page describes the v1 implementation shipped for issue #1028. The design rationale is in `tasks/unsafe-window-materialized-view/design.md` (PR #1027).

## What it gives you

- `safe` table — one row per projection key, last-known state whose ordering has been confirmed by replay.
- `unsafe` table — one row per projection key, the currently-visible "as of latest stream event" state.
- `current` view — unsafe-first, safe fallback: what the application should read by default for diagnostics, including tombstones.
- `current_live` view — same as `current` but filters `_is_deleted = true` rows. This is the view most API consumers should read.
- background hosted service that catches up from the event store into `unsafe` and periodically promotes rows from `unsafe` into `safe` by replaying tag-scoped events.

## The public contract

A projector implements `IUnsafeWindowMvProjector<TRow>`:

```csharp
public sealed class WeatherForecastUnsafeWindowMvV1 : IUnsafeWindowMvProjector<WeatherForecastUnsafeRow>
{
    public string ViewName => "WeatherForecastUnsafeWindow";
    public int ViewVersion => 1;
    public TimeSpan SafeWindow => TimeSpan.FromSeconds(2);

    public UnsafeWindowMvSchema Schema { get; } = new(
        new UnsafeWindowMvColumn("forecast_id", "UUID NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).ForecastId),
        new UnsafeWindowMvColumn("location", "TEXT NOT NULL",
            row => ((WeatherForecastUnsafeRow)row).Location),
        // …
    );

    // The runtime asks for the projection key before it loads any row, so it
    // can look up the current `unsafe` / `safe` row and pass it to `Apply` as
    // `current`. Partial-update events (e.g. a field rename that returns
    // NoChange when `current` is null) rely on this separation working.
    public string? GetProjectionKey(Event ev) => ev.Payload switch
    {
        WeatherForecastCreated c => c.ForecastId.ToString(),
        WeatherForecastUpdated u => u.ForecastId.ToString(),
        LocationNameChanged l => l.ForecastId.ToString(),
        WeatherForecastDeleted d => d.ForecastId.ToString(),
        _ => null
    };

    public UnsafeWindowMvApplyOutcome Apply(WeatherForecastUnsafeRow? current, Event ev) =>
        ev.Payload switch
        {
            WeatherForecastCreated created => new UnsafeWindowMvApplyOutcome.Upsert(
                created.ForecastId.ToString(),
                new WeatherForecastUnsafeRow { /* … */ }),

            WeatherForecastDeleted deleted => new UnsafeWindowMvApplyOutcome.Delete(
                deleted.ForecastId.ToString()),

            _ => new UnsafeWindowMvApplyOutcome.NoChange()
        };

    public IReadOnlyList<ITag> TagsForProjectionKey(string projectionKey) =>
        Guid.TryParse(projectionKey, out var id)
            ? [new WeatherForecastTag(id)]
            : [];
}
```

Four responsibilities:

1. **Schema** — business columns (framework adds metadata columns on top).
2. **GetProjectionKey** — extract the target row's projection key from an event payload so the framework can look up the current row before folding. Returns `null` for events the projector does not care about.
3. **Apply** — deterministic fold `(TRow?, Event) → Outcome`. The framework calls it in two places: the stream apply path and the safe-promotion replay loop. One implementation, two call sites, so the two cannot drift. The `ProjectionKey` returned by an `Upsert` / `Delete` outcome must match the one returned by `GetProjectionKey` for the same event — the runtime fails fast if they differ.
4. **TagsForProjectionKey** — the tags the promotion worker uses to fetch all events that affect a projection key during replay. For an aggregate-centric view this is usually one tag per row.

`Outcome` is a closed union: `NoChange`, `Upsert(key, row)`, or `Delete(key)`. A `Delete` outcome logically retires the row — the framework writes a tombstone in `safe` and hides the row from `current_live`.

## Registration

```csharp
builder.Services.AddSekibanDcbUnsafeWindowMv<WeatherForecastUnsafeWindowMvV1, WeatherForecastUnsafeRow>(
    builder.Configuration,
    connectionStringName: "DcbMaterializedViewPostgres");
```

The extension:

- registers the projector as a singleton,
- builds the schema resolver (physical table / view names),
- registers the initializer (DDL + startup validation),
- registers the catch-up worker and the promotion worker,
- starts a hosted service that runs `catch-up → promote → idle` on a loop.

The runtime requires `IEventStore` and `IEventTypes` from the host — these are already registered by `AddSekibanDcbPostgresWithAspire` / `AddSekibanDcbNativeRuntime`.

## Physical schema (DDL generated by the framework)

```
sekiban_uwmv_{view}_v{n}_safe
    _projection_key    TEXT PRIMARY KEY
    <business columns>
    _is_deleted            BOOLEAN NOT NULL DEFAULT FALSE
    _last_sortable_unique_id TEXT NOT NULL
    _last_event_version    BIGINT NOT NULL
    _last_applied_at       TIMESTAMPTZ NOT NULL
    _safe_confirmed_at     TIMESTAMPTZ NOT NULL

sekiban_uwmv_{view}_v{n}_unsafe
    _projection_key    TEXT PRIMARY KEY
    <business columns>
    _is_deleted            BOOLEAN
    _last_sortable_unique_id TEXT NOT NULL
    _last_event_version    BIGINT NOT NULL
    _last_applied_at       TIMESTAMPTZ NOT NULL
    _unsafe_since          TIMESTAMPTZ NOT NULL
    _safe_due_at           TIMESTAMPTZ NOT NULL
    _needs_rebuild         BOOLEAN NOT NULL DEFAULT FALSE
```

Business columns are validated at startup (`information_schema.columns`): missing / renamed columns fail-fast rather than surfacing as runtime errors during event application.

## Stream apply flow (hot path)

```
event arrives
  └─ projectionKey = projector.GetProjectionKey(ev)
     └─ null → projector is not interested, skip
  └─ SELECT … FROM unsafe WHERE _projection_key = @key FOR UPDATE
     ├─ row exists AND incoming SUID <= unsafe SUID:
     │     set `_needs_rebuild = true` and return
     │     (do NOT overwrite the newer row with an older event)
     ├─ row exists AND incoming SUID  > unsafe SUID:
     │     hydrate `current` from the unsafe row
     └─ row does not exist:
        └─ SELECT … FROM safe WHERE _projection_key = @key FOR UPDATE
           ├─ safe row exists AND incoming SUID <= safe SUID:
           │     mirror safe into unsafe with `_needs_rebuild = true`
           │     (keeps current / current_live consistent with safe; the
           │      promoter will full-replay on its next pass)
           └─ safe row exists AND incoming SUID  > safe SUID:
                 hydrate `current` from the safe row
  └─ outcome = projector.Apply(current, ev)
     ├─ NoChange → return
     ├─ Upsert (key, row)   → upsert unsafe with business columns,
     │                        _last_event_version = previous + 1,
     │                        _safe_due_at = NOW() + SafeWindow
     └─ Delete (key)        → upsert tombstone in unsafe,
                              retaining the last-known business columns
                              (from current / safe) so NOT NULL
                              constraints still hold
```

Older-SUID events never silently overwrite newer rows; they set `_needs_rebuild` so the promotion worker knows this key needs a full replay on its next pass. The `_last_event_version` column increments on every stream apply so reads of `unsafe` during the safe window still carry a monotonic version.

## Safe promotion (correctness path)

```
every N seconds (hosted service loop):
  BEGIN;
    SELECT _projection_key, _needs_rebuild FROM unsafe
    WHERE _safe_due_at <= NOW()
    ORDER BY _safe_due_at
    LIMIT @batch
    FOR UPDATE SKIP LOCKED;

    for each (key, needsRebuild):
      if needsRebuild:
          # Full replay: recover from reordered / late-arriving events.
          current    = null
          startSuid  = null
          isDeleted  = false
      else:
          read current safe row (may be null)
          current    = hydrate(safeRow)
          startSuid  = safe._last_sortable_unique_id
          isDeleted  = safe._is_deleted

      read events via ReadSerializableEventsByTagAsync(tag, since = startSuid)
      for each event in SUID order: Apply(current, ev) → update current / tombstone flag
      upsert safe (including tombstones — _is_deleted survives)
      delete corresponding unsafe row
  COMMIT;
```

Key design decisions:

- **Replay from safe row's SUID**, not from the beginning of time. A long-lived aggregate with 10 000 events does not pay 10 000 events per promotion.
- **Single transaction** per batch. If a replica crashes mid-promotion the transaction rolls back and another replica picks up the same keys on the next tick via `FOR UPDATE SKIP LOCKED`.
- **Delete keeps the business columns** in safe alongside `_is_deleted = true`. Recreating the same key writes a new Upsert, flipping `_is_deleted` back to false.

## Delete / recreate

- `Apply` returns `Delete(key)` → framework writes a tombstone into unsafe. Business columns are **retained** from whichever of the unsafe row or (when unsafe is empty) the safe row existed, so user-declared `NOT NULL` constraints keep holding and diagnostics tooling that reads `current` still sees the last known state alongside `_is_deleted = true`.
- If neither a prior unsafe row nor a prior safe row exists, the delete is a no-op (there is nothing to tombstone yet; any later-arriving create for the same id becomes the first Upsert).
- After promotion, safe holds the tombstone (business columns preserved from the last Upsert before the delete, `_is_deleted = true`).
- `current_live` filters `_is_deleted = true`, so normal reads no longer see the row.
- If a later event recreates the same projection key (e.g. `WeatherForecastCreated` with the same id), `Apply` returns an Upsert. The stream apply path overwrites the tombstone in unsafe; promotion folds the recreate on top of the tombstone safe row, flipping `_is_deleted` back to false and writing the new business columns.

## Scope (issue #1028)

- Postgres only. Other providers are out of scope for v1.
- Single-table per projector, one row per projection key. Multi-table or fan-out projectors are explicitly out of scope and should use the classic MV for now.
- No build-time analyzers. Runtime validation at startup is the enforcement point.
- No tombstone purge job. Tombstones stay in `safe` until the projector is rebuilt (view version bump).
- The classic `IMaterializedViewProjector` remains the internal fast-path for existing projectors; no plan to deprecate it.

Further extensions (multi-table fan-out, dynamic safe-window heuristics interop, tombstone purge, non-Postgres providers) are tracked as follow-up issues rather than inside this v1.
