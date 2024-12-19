namespace Sekiban.Infrastructure.IndexedDb.Databases;

public interface ISekibanIndexedDbContext
{
    Task WriteEventAsync(DbEvent payload);
    Task<DbEvent[]> GetEventsAsync(DbEventQuery query);
    Task RemoveAllEventsAsync();

    Task WriteDissolvableEventAsync(DbEvent payload);
    Task<DbEvent[]> GetDissolvableEventsAsync(DbEventQuery query);
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
    Task<DbBlob> GetMultiProjectionStateBlobAsync(string blobName);

    Task WriteSingleProjectionStateBlobAsync(DbBlob payload);
    Task<DbBlob> GetSingleProjectionStateBlobAsync(string blobName);

    Task WriteMultiProjectionEventsBlobAsync(DbBlob payload);
    Task<DbBlob> GetMultiProjectionEventsBlobAsync(string blobName);
}
