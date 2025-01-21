using System.Text.Json;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Test.IndexedDb;

public class NodeIndexedDbContext(NodejsEnvironment nodejs, JSReference store) : AbstractSekibanIndexedDbContext
{
    protected override async Task DispatchAsync(string operation) =>
        await DispatchAsync<object, object>(operation, null);

    protected override async Task DispatchAsync<TInput>(string operation, TInput? input) where TInput : default =>
        await DispatchAsync<TInput, object>(operation, input);

    protected override async Task<TOutput?> DispatchAsync<TOutput>(string operation) where TOutput : default =>
        await DispatchAsync<object, TOutput>(operation, null);

    protected override async Task<TOutput?> DispatchAsync<TInput, TOutput>(string operation, TInput? input)
        where TInput : default
        where TOutput : default
    {
        var result = await nodejs.RunAsync(async () =>
        {
            var jsInput = input is null ? JSValue.Null : JsonSerializer.Serialize(input);

            var jsOutput = await store.GetValue()[operation].Call(JSValue.Undefined, jsInput).CastTo<JSPromise>().AsTask();

            var output = jsOutput.IsNull() ? default : JsonSerializer.Deserialize<TOutput>(jsOutput.GetValueStringUtf16());
            return output;
        });

        return result;
    }
}
