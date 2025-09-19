# Consistency Improvement Proposals

- Implement a persistent decision log (or idempotent confirmation queue) so TagConsistentActors can recover the outcome of in-flight reservations after process restarts or coordinator failures.
- Add retry/backoff logic for `ConfirmReservationAsync` and `CancelReservationAsync`, coupled with metrics that surface the success/failure rate of these operations.
- Extend `IEventStore.WriteEventsAsync` contracts with transactional capability metadata so the executor can distinguish between fully atomic backends (e.g., Postgres) and best-effort stores (e.g., Cosmos DB) and adjust compensation strategies accordingly.
- Introduce correlation identifiers that propagate from commands into `EventMetadata`, logs, and telemetry, enabling end-to-end tracing of reservation conflicts and persistence failures.
- Capture per-tag contention metrics (reservation wait time, rejection rate, expiry count) to highlight hotspots and guide boundary adjustments.
- Add configurable reservation and persistence timeouts to protect against stuck calls, along with circuit-breaker style guards for unhealthy TagConsistentActors or event stores.
