# Sekiban DCB 10.23.0

- Version: `10.23.0`. 26 DCB library packages. Targets net9.0 and net10.0. `Microsoft.Orleans.*` 10.3.1 (unchanged).
- No data or schema migration is required for the upgrade. Recompile against 10.23.0; see "Check before upgrading" for one signature change and for behaviour that needs your attention.

## Fixes

- **Multi-projection safe state and input-mutating projectors** (issue #1253). A projector whose payload mutates and returns its input could corrupt the safe state. The fix is opt-in per payload: mark the payload with `IMutatesProjectionInput`. See "Check before upgrading".
- **Lost update under clock skew between processes.** Event ids are now allocated above the observed reserved positions of the consistency tags the command writes. See "Check before upgrading" for limits.
- **Stale tag version cache after confirm** (issue #1276). Confirmation invalidates the cache under the reservation lock; subsequent access refreshes it authoritatively under that lock.
- **Cosmos DB: latest tag lookup** now continues through empty result pages instead of reporting no tag.
- **Orleans `MultiProjectionGrain` now passes every actor option through.**
- **Rebuilding a tombstoned checkpoint**: resumes incrementally after a partial force-full catch-up, persists on catch-up completion, clears the durable rebuild marker on the streaming persist path, and adopts the checkpoint slot under a pending rebuild marker.
- **The configured blob prefix now applies to snapshot blobs** (Azure Blob Storage and S3). See "Check before upgrading".
- When activation finds a missing snapshot after a known projector version change, it logs the rebuild as information. A missing snapshot for the same or an unknown previous version still logs a warning.

## Configurable and opt-in features (existing defaults preserved)

- **Hot-only catch-up checkpoint cadence** is now configurable: `HotCatchUpPersistMaxFetchedEvents` (default 5000) and `HotCatchUpPersistMaxIntervalSeconds` (default 300).
- **Bounded first-query wait during catch-up**: `FirstQueryCatchUpMaxWaitMs` (default 0, off). When you enable it, callers must handle the pending result: on expiry, state and snapshot calls return an error result and queries throw `InvalidOperationException` with the catch-up-in-progress prefix; retry later. The bound does not include activation and request queuing, and a query that succeeds has the ordinary projection lag, not guaranteed freshness. See `docs/dcb_llm/13_common_issues.md`, "First query times out during catch-up (SEK-G90; #1253 item 5)".
- **`OffloadKeyEnumerator`** lists the snapshot blob keys referenced by the supplied multi-projection state store in its current ServiceId. It can report `Undecodable` entries, and absence from one enumeration does not by itself permit deletion. See `docs/dcb_llm/04_multiple_aggregate_projector.md`, "Maintenance-window snapshot blob pruning (#1253 item 3)".
- **PostgreSQL tag-head fence derived from reservations**: `TagConsistencyFenceMode.DeriveFromReservations` (default `Off`). It rejects a stale write at the storage layer for consistency tags that the command both read (or was given an explicit position for) and attaches to the events it writes, for example when two activations of one tag grain exist. Tags the command writes without having read them, and non-consistency tags, are not fenced; it is not a read-set guarantee. Enabling it has prerequisites: keep the mode off, drain writers that bypass it, provision the service enablement epoch, then enable; without the epoch commands fail before the handler runs. On the Orleans executor, typed commands can select the mode per command through `IConditionalCommandExecutor`; serialized commits use the global mode. See `docs/dcb_llm/13_common_issues.md`, "Choosing between fast and strict tag consistency".

## Check before upgrading

- **`MultiProjectionGrainStatus` signature.** This positional record gained three members with default values (`FirstQueryCatchUpPending`, `CatchUpTargetPosition`, `LastBackgroundCatchUpError`). Code that reads its properties or constructs it only needs recompilation. Code that deconstructs it positionally must be updated. Assemblies built against 10.22.0 that construct or deconstruct it must be rebuilt.
- **Projectors that mutate their input.** Upgrading alone does not protect them. Mark the payload with `IMutatesProjectionInput`; make `GenerateInitialPayload` return a fresh instance on every call; if you construct the dual-state wrapper yourself, use the overload that takes `DcbDomainTypes` (the older overload throws for marked payloads). A marked payload is cloned when the wrapper is constructed, when a snapshot is restored, and every time safe and unsafe state are reconciled, including after each in-order safe event during catch-up or rebuild. Each clone costs time proportional to the state size. Unmarked projectors keep the previous behaviour and cost. To find unmarked projectors that mutate safe state, temporarily enable `VerifySafeStateIsolation` (default off): it compares the serialized safe state around projection and throws `InvalidOperationException` on a change. The diagnostic adds serialization cost, and non-deterministic serialization can cause false positives. See `docs/dcb_llm/04_multiple_aggregate_projector.md`, "Dual-State Convergence & Safe-Window Graduation (SEK-G18)".
- **Clock skew.** Event ids are a logical time and can run ahead of the wall clock. While they do, multi-projections keep those events in the unsafe window and cold event export waits until the wall clock catches up. An observed stored far-future position remains the generator's floor after clock correction, without a restart. The reservation-based fix covers observed positions of written consistency tags, not read-only tags, unread written tags, or non-consistency tags; direct head reads are not tracked. It does not repair events that are already stored out of order. See `docs/dcb_llm/13_common_issues.md`, "Clock skew between processes (SEK-G116)".
- **Blob prefix.** If you configure a prefix, new snapshot blobs are written under `{prefix}/...`. Blobs written earlier stay at their old keys and remain readable; no migration is needed. Identical content is stored once more under the prefix. For content-addressed (seekable) writes with a prefix, a key longer than 512 characters is rejected before upload; with the name and version lengths PostgreSQL allows, a prefix of at most 57 characters stays within that limit. Provider naming rules still apply. Without a prefix nothing changes.
- **Materialized views.** With the built-in registry stores (PostgreSQL, SQL Server, MySQL, SQLite), applying a batch now locks the registry rows; non-stream application also re-checks the expected position. This locking is always on. A custom `IMvRegistryStore` keeps the previous behaviour unless it implements the new members. `MvCatchUpOutcome` gains `Superseded`; add a branch to exhaustive switches.
- **Custom store implementations.** Interface members added in this release have default implementations.

## Documentation

- Duplicate grain activations, the Orleans grain directory options, and the fast and strict consistency choices.
- Detecting and recovering a permanently tombstoned checkpoint.
- Pruning snapshot blobs in a maintenance window, and the local snapshot read cache.
- Turn-length warnings during catch-up.
