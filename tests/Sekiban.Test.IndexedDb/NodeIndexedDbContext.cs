using System.Text.Json;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Test.IndexedDb;

public class NodeIndexedDbContext(NodejsEnvironment nodejs, JSReference runtime) : ISekibanIndexedDbContext
{
    public async Task<DbEvent[]> GetEventsAsync(DbEventQuery query)
    {
        var events = nodejs.Run(() => {
            var result = runtime.GetValue()["getEventsAsync"].Call(JsonSerializer.Serialize(query));
            return JsonSerializer.Deserialize<DbEvent[]>(result.GetValueStringUtf16());
        });

        await Task.CompletedTask;

        return events!;
    }

    public async Task WriteEventAsync(DbEvent payload)
    {
        nodejs.Run(() => {
            runtime.GetValue()["writeEventAsync"].Call(JsonSerializer.Serialize(payload));
        });
        await Task.CompletedTask;
    }
}
