# DCB Materialized View — Open Questions

Open questions from the design discussion, grouped by category. Each is tagged as `[PoC]` (must be decided before PoC starts), `[impl]` (can be decided during implementation), or `[later]` (can be deferred to a post-PoC phase).

---

## Naming and public surface

- `[PoC]` Should the core interface be `IMaterializedViewProjector` or simply `IMvProjector`? The longer form is more searchable, the shorter form is easier to type. **Current preference: `IMaterializedViewProjector`** with `Mv*` prefix for supporting types.
- `[PoC]` Is `ApplyToViewAsync` the right method name, or would `MaterializeAsync` / `HandleAsync` / `ApplyEventAsync` be clearer? **Current preference: `ApplyToViewAsync`** — unambiguous and mirrors existing DCB verb style.
- `[PoC]` Are `IMaterializedViewProjector` / `IMvApplyContext` / `MvSqlStatement` the cross-language ABI? **No.** They are the .NET authoring API. Wasm/remote execution will use a separate serialized runtime protocol and host adapters.
- `[impl]` Should `MvSqlStatement` be a `record struct` or a regular `record`? Value type avoids allocations but complicates `List<>.Add()` scenarios.
- `[impl]` Should `MvTable` be sealed? (Current design says yes.)
- `[later]` Does `MvRowMapper<T>` need async variants? Probably not for PoC.

## Safe window handling

- `[PoC]` For PoC, use "Option A — delay-apply": only apply events older than the safe window. Confirmed.
- `[impl]` Should the safe window value come from DCB's existing `SortableUniqueId.SafeMilliseconds` constant (5000) or be configurable per MV?
- `[later]` Will MVs ever need Option B (apply-immediately with rollback)? Defer until a real use case emerges.

## Idempotency and row metadata

- `[PoC]` Are row metadata columns (`_last_sortable_unique_id`, `_last_applied_at`) enforced by the framework, or purely recommended? **Current preference: recommended via XML doc + helper constants, not enforced.**
- `[PoC]` Should the primary idempotency mechanism be:
  - (a) Registry `current_position` (framework-driven, transactional with writes)
  - (b) Row `_last_sortable_unique_id < @sid` guards (user-driven, fine-grained)
  - **Current preference: both. (a) handles whole-batch atomicity, (b) handles out-of-order replays within a batch and cross-tool protection.**
- `[impl]` Should there be a helper method like `ctx.BuildIdempotencyGuard("_last_sortable_unique_id")` to generate the common `WHERE` snippet? Might reduce boilerplate but risks becoming a mini-DSL.

## Registry and state

- `[PoC]` Is the two-table design (`sekiban_mv_registry` + `sekiban_mv_active`) the right shape, or should it be a single table with an `is_active` flag? **Current preference: two tables. Single-row-per-view `sekiban_mv_active` simplifies atomic switchover.**
- `[impl]` Does the registry schema itself need a version field (meta-meta)? Suggested solution: hardcode a `SCHEMA_VERSION = 1` constant; if it ever needs to change, write a migration in a new PR.
- `[impl]` Should `sekiban_mv_registry.metadata JSONB` be used for anything specific in PoC, or left as an extensibility hook?

## Error handling and recovery

- `[PoC]` On SQL execution failure: transaction rollback + retry on next worker iteration. Confirmed.
- `[PoC]` Must apply-time reads run in the same transaction/snapshot as the returned writes and registry update? **Yes.** This is required for a deterministic read-modify-write cycle.
- `[impl]` What is the retry policy? Exponential backoff? Max retries? For PoC, a simple "retry with 1s delay up to N times, then halt" is enough.
- `[impl]` How does the operator re-start a halted worker after fixing a poison event? Manual `UPDATE sekiban_mv_registry SET status = 'catching_up'` is acceptable for PoC.
- `[later]` Poison event handling: skip-and-log, dead-letter, halt-and-wait. PoC halts the worker.

## ORM / `IMvRow` details

- `[PoC]` What types must `IMvRow` support out of the box? Minimum: `Guid`, `string`, `int`, `long`, `decimal`, `double`, `bool`, `DateTimeOffset`, `byte[]`, null-allowing variants, `GetAs<T>` for JSONB. Confirmed.
- `[impl]` Should `GetString` on a `null` column throw or return empty string? **Current preference: throw**, and require `GetStringOrNull` for nullable columns. Matches Dapper/EF behavior.
- `[impl]` How does `IMvRow` represent PostgreSQL-specific types like `interval`, `cidr`, `tsvector`? Via `GetAs<T>` with user-provided type mapping; PoC ignores these.
- `[impl]` Should the reflection-based `MvRowMapper<T>` support `init`-only properties and record primary constructors? **Yes**, via expression-tree inspection of the canonical constructor. This is a small amount of extra complexity that saves users from writing a static factory for every row type.
- `[later]` `[GeneratedMvRowMapping]` source generator — evaluate after PoC to see if the reflection version is "fast enough".

## Cross-view reads

- `[impl]` What happens when `GetDependencyViewTable("Foo", "bar")` is called but `Foo` is not present in the pinned dependency map? **Current preference: throw with a descriptive error.** Silent fallback to live active resolution would break replay determinism.
- `[impl]` When is the dependency version map captured? **Current preference: at activation time or another explicit version-management checkpoint**, then persisted in metadata so replays see the same dependency versions.
- `[later]` Should cross-view reads be cached within a single `ApplyToViewAsync` call? E.g., if the user calls `GetDependencyViewTable("CustomerSummary", "customers")` three times, the resolution should be cached once per apply.
- `[later]` How are dependencies between MVs declared? Attribute-based (`[DependsOnMv(typeof(CustomerSummaryMv))]`), DI-based, or auto-detected by scanning code for `GetDependencyViewTable` calls? Defer.
- `[later]` Cycle detection — when? (compile-time analyzer? DI-time validation? first-access runtime check?)

## Version management

- `[PoC]` For PoC, only v1 exists. No activation API needed beyond a manual `UPDATE sekiban_mv_active`.
- `[later]` Should version switching be gated by a "ready-for-activation" assertion (both versions reached the same position)?
- `[later]` Retire cleanup policy: immediate drop, 7-day grace period, or manual.

## DI and configuration

- `[PoC]` Options type: `MvOptions` with `PhysicalNameResolver`, `SafeWindowMs`, `BatchSize`, `PollInterval`, `TablePrefix`.
- `[impl]` Should MV projector registration be typed (`services.AddMaterializedView<OrderSummaryMvV1>()`) or collection-based (`services.AddMaterializedViews(typeof(OrderSummaryMvV1), typeof(CustomerSummaryMvV1))`)? Both are trivial; decide in implementation.
- `[impl]` Does each MV need its own `IDbConnection` factory, or does the whole subsystem share one? For PoC, share one. Multi-database scenarios are later.

## Runtime protocol / WASM

- `[later]` WASM entirely out of PoC scope. The design now explicitly separates .NET authoring interfaces from the future serialized runtime protocol so that the PoC does not paint us into a corner.
- `[impl]` What should the first runtime protocol message set look like? Current preference: apply request/response, read request/response, statement list response, all JSON in the first iteration.
- `[later]` When WASM is added: native + WASM mode selection via DI config, similar to what `0216_multiprojection_wasm_abstraction` does for multi-projections.

## Interaction with existing code

- `[PoC]` Will any existing files be modified? **No.** Only new files in new projects.
- `[PoC]` Will `Sekiban.slnx` be modified to include the new projects? **Yes**, in the implementation PR (not this design PR).
- `[impl]` Will `Directory.Packages.props` need new entries? `Dapper` is likely already there; `MessagePack` is not needed for PoC.
- `[impl]` Will `Sekiban.Dcb.Postgres` be affected? **No**, but both packages will share a Postgres instance and coordinate their DDL through different table prefixes (`dcb_*` vs `sekiban_mv_*`).

## Testing

- `[PoC]` Testcontainers for Postgres as the integration test harness. Confirmed.
- `[impl]` Should unit tests for `ApplyToViewAsync` use a fake `IMvApplyContext` or a real in-memory one (e.g., SQLite)? Fake context is simpler and sufficient for PoC.
- `[impl]` Should there be a "golden test" suite that feeds a known event stream and compares the resulting SQL list against expected SQL? Would be great for regression. Defer to post-PoC.

## Documentation

- `[PoC]` Every new public type gets XML doc comments.
- `[PoC]` A short `README.md` in `Sekiban.Dcb.MaterializedView` explaining how to author an MV and register it in DI.
- `[impl]` Migration guide for converting an existing `ICoreMultiProjector<T>` to a corresponding MV, with AI-assisted translation prompts.
