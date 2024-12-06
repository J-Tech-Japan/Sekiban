using System.Text.Json;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Test.IndexedDb;

public class NodeIndexedDbContext(NodejsEnvironment nodejs, JSReference runtime) : ISekibanIndexedDbContext
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


    private async Task DispatchAsync(string operation) =>
        await DispatchAsync<object, object>(operation, null);

    private async Task DispatchAsync<TInput>(string operation, TInput? input) =>
        await DispatchAsync<TInput, object>(operation, input);

    private async Task<TOutput?> DispatchAsync<TOutput>(string operation) =>
        await DispatchAsync<object, TOutput>(operation, null);

    private async Task<TOutput?> DispatchAsync<TInput, TOutput>(string operation, TInput? input)
    {
        var result = await nodejs.RunAsync(async () =>
        {
            var jsInput = input is null ? JSValue.Null : JsonSerializer.Serialize(input);

            var jsOutput = await runtime.GetValue()[operation].Call(JSValue.Undefined, jsInput).CastTo<JSPromise>().AsTask();

            var output = jsOutput.IsNull() ? default : JsonSerializer.Deserialize<TOutput>(jsOutput.GetValueStringUtf16());
            return output;
        });

        return result;
    }
}
