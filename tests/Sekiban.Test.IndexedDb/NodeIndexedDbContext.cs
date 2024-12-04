using System.Text.Json;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
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

    public async Task RemoveAllDissolvableEventsAsync()
    {
        // TODO
        await Task.CompletedTask;
    }

    public async Task RemoveAllCommandsAsync()
    {
        // TODO
        await Task.CompletedTask;
    }

    public async Task RemoveAllMultiProjectionSnapshotsAsync()
    {
        // TODO
        await Task.CompletedTask;
    }

    public async Task RemoveAllSingleProjectionSnapshotsAsync()
    {
        // TODO
        await Task.CompletedTask;
    }

    private async Task DispatchAsync(string operation) =>
        await DispatchAsync<object, object>(operation, string.Empty);

    private async Task DispatchAsync<TInput>(string operation, TInput input) =>
        await DispatchAsync<TInput, object>(operation, input);

    private async Task<TOutput?> DispatchAsync<TOutput>(string operation) =>
        await DispatchAsync<object, TOutput>(operation, string.Empty);

    private async Task<TOutput?> DispatchAsync<TInput, TOutput>(string operation, TInput input)
    {
        await Task.CompletedTask;

        var result = nodejs.Run(() =>
        {
            var result = runtime.GetValue()[operation].Call(JsonSerializer.Serialize(input));
            return JsonSerializer.Deserialize<TOutput>(result.GetValueStringUtf16());
        });

        return result;
    }
}
