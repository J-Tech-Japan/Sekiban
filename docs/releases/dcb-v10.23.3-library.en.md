# Sekiban DCB 10.23.3

- Version: `10.23.3`. 26 DCB library packages. Targets net9.0 and net10.0. `Microsoft.Orleans.*` 10.3.1 (unchanged).
- No data or schema migration is required, and no public API changed.

## Fix

- **Orleans materialized views keep a stream notification after partial catch-up progress** (issue #1339). When a catch-up batch applied some events but stopped before the newest event announced by the stream (for example because of the batch size), `MaterializedViewGrain` dropped that notification. The next read that found no event it could apply yet then ended catch-up. The announced event was read again when another notification or `RefreshAsync` arrived, or by the idle poll of the event store, which becomes eligible `max(PollInterval, SafeWindowMs)` after the last catch-up attempt (5 seconds with the default options). No event was lost. The grain now keeps the notification until the applied position reaches it.

## Check before upgrading

- While such a notification is outstanding and a catch-up read makes no progress, `RefreshAsync` now waits `PollInterval` between retries instead of 10 ms. The existing limit (`CatchUpStallThreshold`) still ends the wait.
- If the announced event never becomes readable after partial progress, catch-up now stops after `CatchUpStallThreshold` and reports the notification in the grain status error, as 10.23.2 already did when a read applied nothing.

## Templates

Templates for 10.23.3 are released separately, after the libraries.
