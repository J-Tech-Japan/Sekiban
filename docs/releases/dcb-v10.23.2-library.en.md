# Sekiban DCB 10.23.2

- Version: `10.23.2`. 26 DCB library packages. Targets net9.0 and net10.0. `Microsoft.Orleans.*` 10.3.1 (unchanged).
- No data or schema migration is required, and no public API changed. Applications that use cold event hybrid read get one behaviour change: see "Check before upgrading". Applications that do not use it are unaffected by that change.

## Changes

- **Cold events are now read by service-scoped consumers** (issue #1322). `AddSekibanDcbColdEventHybridRead()` used to replace only the registered `IEventStore`. It now also decorates the registered `IEventStoreFactory`, so a store created for a service reads cold segments and then the hot tail. This covers classic materialized-view catch-up (PostgreSQL, SQL Server, MySQL, SQLite), durable subscriptions, and multi-projection catch-up that takes its store from the factory. Until now these read only the hot store, even with cold events enabled.
- **Reading cold events in small batches makes one pass over each segment.** A list read (`ReadAllSerializableEventsAsync` with a maximum count) that stops inside a cold segment keeps the rest of that segment in memory and serves the following reads from it. Reading a segment of 100,000 events in batches of 100 opened the segment 1,000 times before and opens it once now. This was measured with a test storage that counts calls; elapsed time was not measured.

## Fix

- **DynamoDB: automatic table creation tolerates a concurrent creator.** When two callers found a table missing and both created it, the second failed with `ResourceInUseException`. If that caller was the hosted initializer, the application did not start. This could happen at the first start against empty storage, inside one process or between instances that start together. The second caller now waits for the table to become active.

## Check before upgrading

Only for applications that call `AddSekibanDcbColdEventHybridRead()`:

- **Factory consumers start reading cold.** There is no setting for this. Register the event store provider before calling `AddSekibanDcbColdEventHybridRead()`: the `IEventStoreFactory` registered last at that moment is decorated; one registered later is not, and when none is registered nothing is decorated and no error is raised.
- **Memory.** A host that reads through the list path (materialized views, durable subscriptions, or your own calls with a maximum count) can now hold parsed cold events in memory: at most one segment for each service read through the factory, and one more for the singleton `IEventStore`. A segment holds up to `SegmentMaxEvents` events (default 100,000). `SegmentMaxBytes` (default 512 MB) limits the payload bytes of a segment when it is exported; it does not limit reader memory, and parsed events take more memory than their payload bytes. In addition, a read that cannot use the retained segment parses a whole segment while it runs, and concurrent reads each do so. The retained segment is released when it has been read to its end, and after two minutes without use, checked at the next list read through the same store. Readers of one service that alternate between different segments do not benefit and parse a whole segment per read. Size these hosts for the segments you export.
- **Cold exists only for the services an exporter runs for.** A store for another service finds no manifest and reads hot, as before.
- **Failures.** As before, a cold segment that cannot be opened or parsed makes the read come from the hot store, from the same position, and a failed read does not advance a materialized-view checkpoint. Two details changed: a broken line anywhere in a segment, not only in the requested part, now causes the fallback for that read; and a manifest that cannot be deserialized makes the read throw, which now also reaches materialized views and durable subscriptions. As before, hybrid read does not verify that cold storage is complete: a manifest with a gap between segments, or a segment that parses but holds fewer events than its manifest entry says, is not detected, and the read continues past it. Consumers that now read cold therefore rely on the exported segments being intact, where they used to read every event from the hot store.
- **Multi-projection catch-up through the factory** now uses `ColdCatchUpBatchSize` and segment-boundary snapshots, as catch-up through the singleton store already did.

## Documentation

- `docs/dcb_llm/19_cold_events.md` and `20_materialized_view.md` (English and Japanese) describe hybrid read for factory-created stores, the retained segment, and the failure policy. The cold events chapter no longer says that hybrid read removes duplicates by event id and sorts; it partitions at the cold boundary.

## Templates

Templates for 10.23.2 are released separately, after the libraries.
