# Sekiban DCB 10.25.1

- Version: `10.25.1`. 26 DCB library packages. Targets net10.0 and net11.0, as 10.25.0.
- A patch release on the 10.x line, which now receives urgent patches only. Fixes for Unsafe Window materialized views, DynamoDB checkpoint writes and event delivery statistics. The public API and the dependencies are unchanged since 10.25.0, and Orleans stays at 10.4.2.
- No DCB event data migration is required.

## Fixes

- **Unsafe Window materialized views: null values in nullable columns.** On all four providers (PostgreSQL, SQL Server, MySQL, SQLite), writing a row whose nullable column was null failed, because the value was passed to Dapper as `DBNull`, which Dapper rejects. The worker then failed on every retry: the view stayed stuck at that event, also after a restart, and no later event was applied. Null values are now written as null.
- **SQL Server: typed parameters.** A null for a `VARBINARY`, `BINARY` or `IMAGE` column is bound as a typed binary null (an untyped null would be rejected by SQL Server with error 257). A `DateTime` value is bound as `DATETIME2` when the column is a date or time type. Before, it was bound as `DATETIME`: writing a date before 1753 into a `DATE`, `DATETIME2` or `DATETIMEOFFSET` column failed with the same stuck worker, and precision finer than about 3 ms was rounded. Both are now kept; other columns are bound as before. Rows already written to `DATETIME2`, `DATETIMEOFFSET` or `TIME` columns keep their earlier rounding to about 3 ms until the view is rebuilt.
- **PostgreSQL and SQL Server: safe-to-unsafe repair copy.** The copy of the safe table into the unsafe table now runs inside the database (`INSERT ... SELECT`), so values such as `JSONB` and `DATE` keep their type. Before, they went through .NET and the copy could fail (on PostgreSQL, for example `JSONB` with error 42804 or a `DATE` column; on SQL Server, a date before 1753).
- **DynamoDB checkpoint store.** Every conditional checkpoint upsert after the first one, and every rebuilt-checkpoint commit, failed (the tombstone write was not affected), because the request sent an unused expression attribute name (`#u`), which DynamoDB rejects. Persist still reported success, so the stored snapshot stayed at the first one, and the rebuilt snapshot was not committed, so a pending rebuild obligation stayed unmet. The conditional writes now send only the names they use.
- **Event delivery statistics.** `RecordingMultiProjectionEventStatistics` no longer received any event after earlier internal refactors, so `GetEventDeliveryStatisticsAsync` (and the templates' `*/event-statistics` routes) always returned 0. Recording is restored: events are counted when they arrive from the Orleans stream and when they are fetched by catch-up (including `AddEventsAsync`), before deduplication and filtering. The built-in implementation is thread-safe. With the default `NoOpMultiProjectionEventStatistics` nothing is recorded and nothing is allocated, as before. The recording implementation keeps every delivered event id in memory: use it only during development. A custom `IMultiProjectionEventStatistics` implementation is now called again and must be thread-safe, because catch-up can call it outside the grain scheduler; the `Event` it receives carries a placeholder payload, and only `Id`, `EventType`, `SortableUniqueIdValue`, `Tags` and `EventMetadata` are meaningful.

## Upgrading from 10.25.0

- No API change and no data migration: update the package versions to 10.25.1 (see the SQL Server note above for rebuilding views that need full `DATETIME2` precision, and the statistics note for custom implementations). The dependency versions are the same as in 10.25.0.
- Projects generated from the templates reference the library through `SekibanDcbVersion` in `SekibanDcbTemplateVersion.props`; change it to `10.25.1`. New projects reference 10.25.1 once the 10.25.1 templates are released.
- The same fixes are planned for the 11 line in 11.0.1-preview02.

## Templates

Templates for 10.25.1 are released separately, after the libraries.
