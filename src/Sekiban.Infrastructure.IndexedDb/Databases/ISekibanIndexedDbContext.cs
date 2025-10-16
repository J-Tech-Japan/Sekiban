namespace Sekiban.Infrastructure.IndexedDb.Databases;

public interface ISekibanIndexedDbContext
{
    Task WriteEventAsync(DbEvent payload);
    Task<DbEvent[]> GetEventsAsync(DbEventQuery query);
    Task<DbEvent[]> GetEventsAsyncChunk(DbEventQuery query, int chunkSize, int skip);
    Task RemoveAllEventsAsync();

    Task WriteDissolvableEventAsync(DbEvent payload);
    Task<DbEvent[]> GetDissolvableEventsAsync(DbEventQuery query);
    Task<DbEvent[]> GetDissolvableEventsAsyncChunk(DbEventQuery query, int chunkSize, int skip);
    Task RemoveAllDissolvableEventsAsync();

    Task WriteCommandAsync(DbCommand payload);
    Task<DbCommand[]> GetCommandsAsync(DbCommandQuery query);
    Task RemoveAllCommandsAsync();

    Task WriteSingleProjectionSnapshotAsync(DbSingleProjectionSnapshot payload);
    Task<DbSingleProjectionSnapshot[]> GetSingleProjectionSnapshotsAsync(DbSingleProjectionSnapshotQuery query);
    Task RemoveAllSingleProjectionSnapshotsAsync();

    Task WriteMultiProjectionSnapshotAsync(DbMultiProjectionSnapshot payload);
    Task<DbMultiProjectionSnapshot[]> GetMultiProjectionSnapshotsAsync(DbMultiProjectionSnapshotQuery query);
    Task RemoveAllMultiProjectionSnapshotsAsync();

    Task WriteMultiProjectionStateBlobAsync(DbBlob payload);
    Task<DbBlob[]> GetMultiProjectionStateBlobsAsync(DbBlobQuery query);

    Task WriteSingleProjectionStateBlobAsync(DbBlob payload);
    Task<DbBlob[]> GetSingleProjectionStateBlobsAsync(DbBlobQuery query);

    Task WriteMultiProjectionEventsBlobAsync(DbBlob payload);
    Task<DbBlob[]> GetMultiProjectionEventsBlobsAsync(DbBlobQuery query);
}
