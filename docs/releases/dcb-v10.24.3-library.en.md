# Sekiban DCB 10.24.3

- Version: `10.24.3`. 26 DCB library packages. Targets net9.0 and net10.0.
- Fixes for materialized-view initialization ([#1362](https://github.com/J-Tech-Japan/Sekiban/issues/1362)) and for the in-memory event store ([#1340](https://github.com/J-Tech-Japan/Sekiban/issues/1340)), and faster PostgreSQL appends for events with several tags ([#1347](https://github.com/J-Tech-Japan/Sekiban/issues/1347)). The public API and the dependencies are unchanged since 10.24.2, and Orleans stays at 10.4.2.
- No DCB event data migration is required.

## Fixes

- Materialized views with `MvInitializationMode.CreateOrEnsure` no longer stop the host when two components initialize the same database at the same time, for example the catch-up worker and the Orleans materialized-view grain at startup. Before, both ran the schema statements at once, and on PostgreSQL one of them could fail with error 23505 (`pg_type_typname_nsp_index`); with the default `StopHost` behaviour the host stopped. Now the initializers of one database run one after another.
- `InMemoryEventStore.GetLatestTagAsync` returns an empty tag state (version 0) for a tag that has no events, as the PostgreSQL and Cosmos DB stores do. Before, it returned an error, so on the in-memory store every reservation on such a tag failed once any reservation had been confirmed ("has been modified or could not be refreshed authoritatively").

## Performance

- PostgreSQL: an append whose events carry two or more distinct tags now updates the tag heads with a fixed number of set-based statements instead of several statements per tag. In the common case (the tag heads already exist and no repair is needed), an append sends 4 tag-head statements for any number of tags; before, it sent 4 per tag (80 for 20 tags). The lock order, the conflict and repair behaviour, and the results are unchanged, and an append with a single tag runs the same SQL as before. In a local benchmark, appends with 5 tags were about 2 times faster, appends with 20 tags about 2.4 to 3.7 times faster, and 16 concurrent writers sharing one tag plus 4 private tags about 1.8 times faster; single-tag appends were unchanged. No schema change is needed.

## Templates

Templates for 10.24.3 are released separately, after the libraries.
