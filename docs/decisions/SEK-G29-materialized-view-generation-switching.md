# SEK-G29: Materialized-view generation switching

Status: Accepted

## Decision

Sekiban coordinates materialized-view generations per exact service and view. Each version keeps independent physical
tables, progress, and apply machinery. Ordinary reads resolve the durable active pointer once per operation; version-pinned
reads use a separately named diagnostics API.

Forward and ordinary reverse switches use the SEK-G27 eligibility boundary and provider-atomic expected-active/generation
CAS. The old generation is retained. Break-glass rollback is a separate reverse-only operation that waives only checkpoint
freshness/truth while preserving identity, lifecycle, existence, and CAS fencing. It durably records forced kind, reason,
and timestamp and pushes that audit state through the existing G24/G28 source-side observation surface.

## Consequences

- Catch-up and serving can proceed concurrently without sharing checkpoints or adding an apply engine.
- Restart and cross-process safety depend on the provider CAS; process and grain single-flight only bound local work.
- A caller cannot force a forward switch or smuggle a force flag into ordinary activation.
- Observation remains passive and does not resolve, open, or query an MV target database.
- Retained generations consume storage until a future, explicitly out-of-scope cleanup policy removes them.
