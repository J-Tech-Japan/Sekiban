# Sekiban DCB 10.24.3

- Version: `10.24.3`. 26 DCB library packages. Targets net9.0 and net10.0.
- Fixes for materialized-view initialization ([#1362](https://github.com/J-Tech-Japan/Sekiban/issues/1362)) and for the in-memory event store ([#1340](https://github.com/J-Tech-Japan/Sekiban/issues/1340)). The public API and the dependencies are unchanged since 10.24.2, and Orleans stays at 10.4.2.
- No DCB event data migration is required.

## Fixes

- Materialized views with `MvInitializationMode.CreateOrEnsure` no longer stop the host when two components initialize the same database at the same time, for example the catch-up worker and the Orleans materialized-view grain at startup. Before, both ran the schema statements at once, and on PostgreSQL one of them could fail with error 23505 (`pg_type_typname_nsp_index`); with the default `StopHost` behaviour the host stopped. Now the initializers of one database run one after another.
- `InMemoryEventStore.GetLatestTagAsync` returns an empty tag state (version 0) for a tag that has no events, as the PostgreSQL and Cosmos DB stores do. Before, it returned an error, so on the in-memory store every reservation on such a tag failed once any reservation had been confirmed ("has been modified or could not be refreshed authoritatively").

## Templates

Templates for 10.24.3 are released separately, after the libraries.
