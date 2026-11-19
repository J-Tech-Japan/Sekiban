# Sekiban DCB 11.0.1-preview02

- Version: `11.0.1-preview02`. The second preview of the DCB 11 line. 26 DCB library packages. Targets net10.0 and net11.0.
- Fixes for Unsafe Window materialized views, DynamoDB checkpoint writes and event delivery statistics. The public API and the dependencies are unchanged since 11.0.1-preview01.
- No DCB event data migration is required.

## Fixes

- **Unsafe Window materialized views: null values in nullable columns.** On all four providers (PostgreSQL, SQL Server, MySQL, SQLite), writing a row whose nullable column was null failed, because the value was passed to Dapper as `DBNull`, which Dapper rejects. The worker then failed on every retry: no later event was applied, and after a restart the view stayed empty. Null values are now written as null.
- **SQL Server: typed parameters.** A null for a `VARBINARY`, `BINARY` or `IMAGE` column is bound as a binary null; before, it was sent untyped and SQL Server rejected the write (error 257). A `DateTime` value is bound as `DATETIME2` when the column is a date or time type, so dates before 1753 and precision finer than about 3 ms are kept; other columns are bound as before.
- **PostgreSQL and SQL Server: safe-to-unsafe repair copy.** The copy of the safe table into the unsafe table now runs inside the database (`INSERT ... SELECT`), so values such as `JSONB` and `DATE` keep their type. Before, they went through .NET and the copy could fail (for example `JSONB` with error 42804, or a `DATE` column on PostgreSQL).
- **DynamoDB checkpoint store.** Every conditional checkpoint write after the first one failed, because the request sent an unused expression attribute name (`#u`), which DynamoDB rejects. Persist still reported success, so the stored snapshot stayed at the first one and a durable rebuild obligation was not saved. The conditional writes now send only the names they use.
- **Event delivery statistics.** `RecordingMultiProjectionEventStatistics` no longer received any event after earlier internal refactors, so the event-statistics routes always returned 0. Recording is restored for the stream and catch-up paths and is thread-safe. With the default `NoOpMultiProjectionEventStatistics` nothing is recorded and nothing is allocated, as before. The recording implementation keeps every delivered event id in memory: use it only during development.

## Upgrading from 11.0.1-preview01

- No API change and no data migration: update the package versions. This is still a preview, with the same dependency versions as 11.0.1-preview01.
- The same fixes are released for the 10.x line in 10.25.1.

## Templates

The five DCB templates on the 11 line now reference `Sekiban.Dcb` `11.0.1-preview02` (`SekibanDcbVersion` in each template's `SekibanDcbTemplateVersion.props`). As with 11.0.1-preview01, template previews are not tagged or released.
