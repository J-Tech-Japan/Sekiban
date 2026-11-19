# Sekiban DCB 11.0.1-preview02

- Version: `11.0.1-preview02`. The second preview of the DCB 11 line. 26 DCB library packages. Targets net10.0 and net11.0.
- Fixes for Unsafe Window materialized views, DynamoDB checkpoint writes and event delivery statistics. The public API and the dependencies are unchanged since 11.0.1-preview01.
- No DCB event data migration is required.

## Fixes

- **Unsafe Window materialized views: null values in nullable columns.** On all four providers (PostgreSQL, SQL Server, MySQL, SQLite), writing a row whose nullable column was null failed, because the value was passed to Dapper as `DBNull`, which Dapper rejects. The worker then failed on every retry: the view stayed stuck at that event, also after a restart, and no later event was applied. Null values are now written as null.
- **SQL Server: typed parameters.** A null for a `VARBINARY`, `BINARY` or `IMAGE` column is bound as a typed binary null (an untyped null would be rejected by SQL Server with error 257). A `DateTime` value is bound as `DATETIME2` when the column is a date or time type. Before, it was bound as `DATETIME`: writing a date before 1753 into a `DATE`, `DATETIME2` or `DATETIMEOFFSET` column failed with the same stuck worker, and precision finer than about 3 ms was rounded. Both are now kept; other columns are bound as before. Rows already written to `DATETIME2`, `DATETIMEOFFSET` or `TIME` columns keep their earlier rounding to about 3 ms until the view is rebuilt.
- **PostgreSQL and SQL Server: safe-to-unsafe repair copy.** The copy of the safe table into the unsafe table now runs inside the database (`INSERT ... SELECT`), so values such as `JSONB` and `DATE` keep their type. Before, they went through .NET and the copy could fail (on PostgreSQL, for example `JSONB` with error 42804 or a `DATE` column; on SQL Server, a date before 1753).
- **DynamoDB checkpoint store.** Every conditional checkpoint upsert after the first one, and every rebuilt-checkpoint commit, failed (the tombstone write was not affected), because the request sent an unused expression attribute name (`#u`), which DynamoDB rejects. Persist still reported success, so the stored snapshot stayed at the first one and a durable rebuild obligation was not saved. The conditional writes now send only the names they use.
- **Event delivery statistics.** `RecordingMultiProjectionEventStatistics` no longer received any event after earlier internal refactors, so `GetEventDeliveryStatisticsAsync` (and the templates' `*/event-statistics` routes) always returned 0. Recording is restored: events are counted when they arrive from the Orleans stream and when they are fetched by catch-up (including `AddEventsAsync`), before deduplication and filtering. The built-in implementation is thread-safe. With the default `NoOpMultiProjectionEventStatistics` nothing is recorded and nothing is allocated, as before. The recording implementation keeps every delivered event id in memory: use it only during development. A custom `IMultiProjectionEventStatistics` implementation is now called again and must be thread-safe, because catch-up can call it outside the grain scheduler; the `Event` it receives carries a placeholder payload, and only `Id`, `EventType`, `SortableUniqueIdValue`, `Tags` and `EventMetadata` are meaningful.

## Upgrading from 11.0.1-preview01

- No API change and no data migration: update the package versions (see the SQL Server note above for rebuilding views that need full `DATETIME2` precision, and the statistics note for custom implementations). This is still a preview, with the same dependency versions as 11.0.1-preview01.
- The same fixes will also be released for the 10.x line in 10.25.1.

## Templates

The five DCB templates on the 11 line now reference `Sekiban.Dcb` `11.0.1-preview02` (`SekibanDcbVersion` in each template's `SekibanDcbTemplateVersion.props`). Since 11.0.1-preview01, the WithoutResult and Decider templates no longer use `ResultBox` or reference `ResultBoxes`. As with 11.0.1-preview01, template previews are not tagged or released.
