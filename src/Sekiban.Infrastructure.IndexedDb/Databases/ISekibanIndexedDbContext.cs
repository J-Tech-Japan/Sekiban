namespace Sekiban.Infrastructure.IndexedDb.Databases;

public interface ISekibanIndexedDbContext
{
    Task WriteEventAsync(DbEvent payload);

    Task<DbEvent[]> GetEventsAsync(DbEventQuery query);

    Task RemoveAllEventsAsync();

    Task RemoveAllDissolvableEventsAsync();

    Task RemoveAllCommandsAsync();

    Task RemoveAllSingleProjectionSnapshotsAsync();

    Task RemoveAllMultiProjectionSnapshotsAsync();
}
