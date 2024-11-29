using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb;

public class IndexedDbFactory(
    SekibanIndexedDbOptions dbOptions,
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider,
    ISekibanJsRuntime sekibanJsRuntime
)
{
    private string SekibanContextIdentifier()
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }

    public async Task<T> DbActionAsync<T>(Func<ISekibanIndexedDbContext, Task<T>> action) =>
        await action(await GetDbContextAsync());

    public async Task DbActionAsync(Func<ISekibanIndexedDbContext, Task> action) =>
        await action(await GetDbContextAsync());

    private async Task<ISekibanIndexedDbContext> GetDbContextAsync()
    {
        var databaseName = dbOptions.Contexts.Find(m => m.Context == SekibanContextIdentifier())?.DatabaseName ?? SekibanIndexedDbOption.DatabaseNameDefaultValue;
        var cacheKey = $"indexed-db.context.${databaseName}";

        var dbContext = memoryCache.Cache.Get(cacheKey) as ISekibanIndexedDbContext;
        if (dbContext is not null)
        {
            return dbContext;
        }

        dbContext = await sekibanJsRuntime.CreateContextAsync(databaseName);
        memoryCache.Cache.Set(cacheKey, dbContext);

        return dbContext;
    }
}
