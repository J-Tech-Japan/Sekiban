# SEK-G57 implementation report

Task: `sek-g57-implementation`
Issue: [J-Tech-Japan/Sekiban#1189](https://github.com/J-Tech-Japan/Sekiban/issues/1189)
Execution unit: `SEK-G57`
Branch: `codex/sek-g57-active-lifecycle`

## Implementation

- Preserved the serving `Active` lifecycle while catch-up, activation, and status publication race with refreshes.
- Added the additive `MvActiveStatusRestoreRequest` capability with full logical/physical identity, generation, checkpoint-truth, monotonic-progress, and allowed-status validation.
- Added provider-atomic C/R-to-Active restoration for PostgreSQL, MySQL, SQL Server, and SQLite, with rollback-safe savepoints, bounded transient retry, cancellation propagation, and no fallback to a synthetic `Ready` state.
- Established the canonical registry-before-pointer lock order for activation and forced reverse paths, with deterministic barrier coverage for the forward/reverse races.
- Added Orleans/PostgreSQL lifecycle-preservation coverage and updated the English/Japanese materialized-view documentation.

## Verification

The focused commands used the isolated cache `NUGET_PACKAGES=/private/tmp/sek-g57-nuget` and `NUGET_HTTP_CACHE_PATH=/private/tmp/sek-g57-nuget-http`.

- `MvActiveStatusRestoreTests`: `dotnet test ... -f net10.0` — 44 passed; `-f net9.0` — 44 passed.
- `MvGenerationSwitchTests`: `-f net10.0` — 8 passed; `-f net9.0` — 8 passed.
- Existing activation/generation-switch regression filter: `-f net10.0` — 68 passed; `-f net9.0` — 68 passed.
- Existing `VerifyOnly` MultiProvider filter: `-f net10.0` — 32 passed; `-f net9.0` — 32 passed.
- `Sekiban.Dcb.MaterializedView.Tests`: `-f net10.0` — 83 passed; `-f net9.0` — 83 passed.
- `Sekiban.Dcb.MaterializedView.Postgres.Tests` builds succeeded for `net10.0` and `net9.0`.
- `git diff --check` passed.

The local Orleans TestCluster runtime characterization was attempted against the real PostgreSQL container, but deployment stalled after PostgreSQL readiness in this environment. The new Orleans test project therefore has build evidence locally, not a claimed local runtime pass; CI remains the runtime gate.

## Scope

No merge or release action was performed. The G56 sender-local notification outbox is outside this worktree and was not modified.
