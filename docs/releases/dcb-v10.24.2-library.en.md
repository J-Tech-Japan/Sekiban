# Sekiban DCB 10.24.2

- Version: `10.24.2`. 26 DCB library packages. Targets net9.0 and net10.0.
- Fixes for multi-projection snapshot restore and persistence ([#1382](https://github.com/J-Tech-Japan/Sekiban/issues/1382)). The public API and the dependencies are unchanged since 10.24.1, and Orleans stays at 10.4.2.
- No DCB event data migration is required.

## Fixes

- A cancelled snapshot restore no longer forces a full replay. Before, when the restore of a stored multi-projection snapshot was cancelled, the grain handled it like a broken snapshot: it reset the integrity watermark and invalidated the stored snapshot, and the next activation replayed every event. The restore is cancelled when it takes longer than the Orleans activation timeout (30 seconds by default), for example for a large snapshot on a freshly started silo, or when a storage request is cancelled during a restart. Now that activation fails without changing the stored state, and the next activation restores the stored snapshot again. Other restore failures still lead to a full replay, as before.
- If restoring a large snapshot takes longer than the activation timeout, each activation now fails instead of falling back to a full replay, and the grain logs a warning that names the activation timeout. Raise the timeout in the silo configuration so the restore can finish, for example `siloBuilder.Configure<GrainCollectionOptions>(options => options.ActivationTimeout = TimeSpan.FromMinutes(5))`.
- A persist that will not be saved no longer uploads the snapshot payload. Before, while the stored snapshot was ahead of the grain (for example during a replay), each persist uploaded the offloaded payload blob and then skipped the save, which left an unreferenced blob behind each time. Now the grain checks first and builds no snapshot when the save would be skipped. A blob can still be left if the save fails or another writer wins after the upload. The library does not delete blobs; remove blobs left by earlier versions with your own cleanup.
- A full replay after an invalidated snapshot (after a restore failure, a rebuild or a fault reset, or after a restart that finds an invalidated snapshot) saves its checkpoints again. Before, on stores that keep the invalidated snapshot (Cosmos DB, PostgreSQL, SQLite, DynamoDB and in-memory), its old event count blocked every save until the replay passed it, so a restart during the replay started from zero again. Now the replay saves at its normal interval. This also heals a checkpoint whose invalidated snapshot has a larger event count than the event store can replay (for example after an event-store reset): the next activation saves the rebuilt checkpoint, and the manual recovery in the troubleshooting guide is needed only for earlier versions.
- A grain whose checkpoint save was rejected because another writer had invalidated the checkpoint for a full rebuild no longer saves the state it had before. Before, on stores with checkpoint generation support (Cosmos DB, PostgreSQL, SQLite, DynamoDB and in-memory), for example when several clusters share one store, the next save from that grain (on the timer, after a catch-up or on deactivation) could store that outdated state as the checkpoint, without the events that the rebuild was meant to include, and later activations restored it. Now that grain stops saving, and the next query replays all events from the event store, as in a runtime full rebuild; only the replayed state is saved.

## Upgrading

If you upgrade from a version before 10.24.0, read the 10.24.0 release notes first: Orleans 10.4.2 requires upgrading the whole cluster together.

## Templates

Templates for 10.24.2 are released separately, after the libraries. Templates 10.24.1 were not published.
