using System.Text.Json;
using Microsoft.JSInterop;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public class BlazorIndexedDbContext(IJSObjectReference store) : AbstractSekibanIndexedDbContext
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
        var jsInput = input is null ? null : JsonSerializer.Serialize(input);

        var jsOutput = await store.InvokeAsync<string?>(operation, jsInput);

        var output = jsOutput is null ? default : JsonSerializer.Deserialize<TOutput>(jsOutput);

        return output;
    }
}
