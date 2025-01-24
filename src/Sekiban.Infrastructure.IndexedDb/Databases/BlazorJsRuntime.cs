using Microsoft.JSInterop;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public class BlazorJsRuntime(IJSRuntime jsRuntime) : ISekibanJsRuntime
{
    public async Task<ISekibanIndexedDbContext> CreateContextAsync(string context)
    {
        var module = await jsRuntime.InvokeAsync<IJSObjectReference>("import", ISekibanJsRuntime.RuntimePath);
        var store = await module.InvokeAsync<IJSObjectReference>("init", context);
        return new BlazorIndexedDbContext(store);
    }
}
