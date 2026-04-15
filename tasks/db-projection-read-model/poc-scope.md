# DCB Materialized View — PoC Scope

This document defines the minimum viable PoC for DCB Materialized View. The goal of the PoC is to validate the design end-to-end with one real materialized view running against PostgreSQL, without building any of the deferred features.

## PoC goal (one sentence)

> A sample `OrderSummaryMvV1` runs under `DcbOrleans.AppHost`, catches up from the existing DCB event store, writes rows to real `sekiban_mv_ordersummary_v1_orders` / `sekiban_mv_ordersummary_v1_items` tables, and can be queried with plain SQL.

## PoC scope — IN

| Phase | Items included |
|---|---|
| Phase 1 | `IMvRegistryStore`, `MvRegistryEntry`, `MvActiveEntry`, `MvStatus`, `PhysicalNameResolver` |
| Phase 2 | `MvTable`, `MvSqlStatement`, `IMvInitContext`, `IMvApplyContext`, `IMaterializedViewProjector` |
| Phase 3 | `IMvRow`, `IMvRowSet`, `MvRowMapper<T>` (Expression-tree version), `MvColumnAttribute` |
| Phase 4 | Row metadata column conventions (`_last_sortable_unique_id`, `_last_applied_at`), `MvSchemaHelper` |
| Phase 5 | `Sekiban.Dcb.MaterializedView.Postgres` with Dapper-backed implementation of all above interfaces |
| Phase 6 | `MvCatchUpWorker` as `IHostedService`, one worker managing one view |
| Phase 9 | One sample `OrderSummaryMvV1` in `internalUsages/` |
| Phase 10 (subset) | Unit tests for apply logic (no DB) + at least one integration test with Testcontainers |

## PoC scope — OUT (explicitly deferred)

| Deferred | Reason |
|---|---|
| Multiple MV versions coexisting (v1 + v2) | Requires full version manager; not needed to prove the core loop |
| Activation/rollback API | Hardcode v1 as active for PoC |
| Cross-view reads (Phase 8) | Single-view PoC first |
| Read-side query API (Phase 11) | Use `Connection` directly |
| Orleans grain integration | IHostedService is enough for the first proof |
| Source Generator for `MvRowMapper` | Reflection-based mapper is plenty for PoC |
| MessagePack at WASM boundary | JSON is fine for PoC; WASM itself is deferred |
| Multi-tenant physical name override | Use default resolver |
| `IDependsOnMv` topological sort | Single view |
| CLI for activation/retire | Manual SQL updates are fine for PoC |
| CosmosDB/DynamoDB/SQLite backends | Postgres first |

## Acceptance criteria

The PoC is considered successful when all of the following are verifiable:

1. **Registry tables exist**: On first run of `DcbOrleans.AppHost`, `sekiban_mv_registry` and `sekiban_mv_active` are created in PostgreSQL.
2. **View initialization**: `OrderSummaryMvV1.InitializeAsync` successfully runs and creates `sekiban_mv_ordersummary_v1_orders` and `sekiban_mv_ordersummary_v1_items` tables.
3. **Registry entries**: After initialization, `sekiban_mv_registry` contains 2 rows (one per logical table) with `status='catching_up'`. `sekiban_mv_active` contains 1 row pointing at `v1`.
4. **Catch-up**: `MvCatchUpWorker` reads events via `IEventStore.ReadAllSerializableEventsAsync` and applies them. Events older than the safe window (5s) are applied.
5. **Write atomicity**: For each event producing multiple `MvSqlStatement`s, all statements and the registry `current_position` update happen in a single transaction. Verified by intentionally failing a second statement and observing that the first is rolled back.
6. **Idempotency**: Killing and restarting the worker mid-event results in no duplicate writes. The `_last_sortable_unique_id` guard prevents double-apply.
7. **Row data**: After sending a series of test commands (create order → add items → cancel), running `SELECT * FROM sekiban_mv_ordersummary_v1_orders` from `psql` returns the expected row.
8. **Metadata columns**: Every row has a non-null `_last_sortable_unique_id` and `_last_applied_at`.
9. **Registry advances**: `sekiban_mv_registry.current_position` matches the latest event's `SortableUniqueId`.
10. **Unit tests pass**: `ApplyToViewAsync` returns the expected `MvSqlStatement` list for each event type without touching the database.
11. **Integration test passes**: Testcontainers spins up Postgres, the end-to-end flow runs, assertions pass.
12. **Zero changes to existing code**: `git diff main -- dcb/src/Sekiban.Dcb.Core dcb/src/Sekiban.Dcb.Postgres dcb/src/Sekiban.Dcb.Core.Model` shows only additions to `Directory.Build.props` / `Directory.Packages.props` if new packages are added. No modifications to existing classes.

## Sprint estimate

Assuming one full-time developer familiar with DCB internals:

| Phase | Estimate |
|---|---|
| Phase 1 (registry) | 1-2 days |
| Phase 2 (abstractions) | 1 day |
| Phase 3 (IMvRow + mapper) | 2-3 days |
| Phase 4 (metadata conventions) | 0.5 day |
| Phase 5 (Postgres implementation) | 2-3 days |
| Phase 6 (catch-up worker) | 2-3 days |
| Phase 9 (sample app) | 1-2 days |
| Phase 10 (tests) | 2-3 days |
| **Total** | **~2 weeks** |

This includes getting the sample running under `DcbOrleans.AppHost`, writing tests, and iterating on bugs. It does not include deferred phases.

## Non-goal: performance

The PoC does not need to hit any specific throughput target. It only needs to be correct and observable. Performance tuning, MessagePack, batching, and native-path optimizations are all post-PoC work.
