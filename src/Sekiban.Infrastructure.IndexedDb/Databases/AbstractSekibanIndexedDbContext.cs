namespace Sekiban.Infrastructure.IndexedDb.Databases;

public abstract class AbstractSekibanIndexedDbContext : ISekibanIndexedDbContext
{
    public async Task<DbEvent[]> GetEventsAsync(DbEventQuery query) =>
        (await DispatchAsync<DbEventQuery, DbEvent[]>("getEventsAsync", query))!;

    public async Task WriteEventAsync(DbEvent payload) =>
        await DispatchAsync("writeEventAsync", payload);

    public async Task RemoveAllEventsAsync() =>
        await DispatchAsync("removeAllEventsAsync");

    public async Task<DbEvent[]> GetDissolvableEventsAsync(DbEventQuery query) =>
        (await DispatchAsync<DbEventQuery, DbEvent[]>("getDissolvableEventsAsync", query))!;

    public async Task WriteDissolvableEventAsync(DbEvent payload) =>
        await DispatchAsync("writeDissolvableEventAsync", payload);

    public async Task RemoveAllDissolvableEventsAsync() =>
        await DispatchAsync("removeAllDissolvableEventsAsync");

    public async Task<DbCommand[]> GetCommandsAsync(DbCommandQuery query) =>
        (await DispatchAsync<DbCommandQuery, DbCommand[]>("getCommandsAsync", query))!;

    public async Task WriteCommandAsync(DbCommand payload) =>
        await DispatchAsync("writeCommandAsync", payload);

    public async Task RemoveAllCommandsAsync() =>
        await DispatchAsync("removeAllCommandsAsync");

    public async Task<DbSingleProjectionSnapshot[]> GetSingleProjectionSnapshotsAsync(DbSingleProjectionSnapshotQuery query) =>
        (await DispatchAsync<DbSingleProjectionSnapshotQuery, DbSingleProjectionSnapshot[]>("getSingleProjectionSnapshotsAsync", query))!;

    public async Task WriteSingleProjectionSnapshotAsync(DbSingleProjectionSnapshot payload) =>
        await DispatchAsync("writeSingleProjectionSnapshotAsync", payload);

    public async Task RemoveAllSingleProjectionSnapshotsAsync() =>
        await DispatchAsync("removeAllSingleProjectionSnapshotsAsync");

    public async Task WriteMultiProjectionSnapshotAsync(DbMultiProjectionSnapshot payload) =>
        await DispatchAsync("writeMultiProjectionSnapshotAsync", payload);

    public async Task<DbMultiProjectionSnapshot[]> GetMultiProjectionSnapshotsAsync(DbMultiProjectionSnapshotQuery query) =>
        (await DispatchAsync<DbMultiProjectionSnapshotQuery, DbMultiProjectionSnapshot[]>("getMultiProjectionSnapshotsAsync", query))!;

    public async Task RemoveAllMultiProjectionSnapshotsAsync() =>
        await DispatchAsync("removeAllMultiProjectionSnapshotsAsync");

    public async Task WriteMultiProjectionStateBlobAsync(DbBlob payload) =>
        await DispatchAsync("writeMultiProjectionStateBlobAsync", payload);

    public async Task<DbBlob> GetMultiProjectionStateBlobAsync(string blobName) =>
        (await DispatchAsync<string, DbBlob>("getMultiProjectionStateBlobAsync", blobName))!;

    public async Task WriteSingleProjectionStateBlobAsync(DbBlob payload) =>
        await DispatchAsync("writeSingleProjectionStateBlobAsync", payload);

    public async Task<DbBlob> GetSingleProjectionStateBlobAsync(string blobName) =>
        (await DispatchAsync<string, DbBlob>("getSingleProjectionStateBlobAsync", blobName))!;

    public async Task WriteMultiProjectionEventsBlobAsync(DbBlob payload) =>
        await DispatchAsync("writeMultiProjectionEventsBlobAsync", payload);

    public async Task<DbBlob> GetMultiProjectionEventsBlobAsync(string blobName) =>
        (await DispatchAsync<string, DbBlob>("getMultiProjectionEventsBlobAsync", blobName))!;


    protected abstract Task DispatchAsync(string operation);

    protected abstract Task DispatchAsync<TInput>(string operation, TInput? input)
        where TInput : notnull;

    protected abstract Task<TOutput?> DispatchAsync<TOutput>(string operation)
        where TOutput : notnull;

    protected abstract Task<TOutput?> DispatchAsync<TInput, TOutput>(string operation, TInput? input)
        where TInput : notnull
        where TOutput : notnull;
}
