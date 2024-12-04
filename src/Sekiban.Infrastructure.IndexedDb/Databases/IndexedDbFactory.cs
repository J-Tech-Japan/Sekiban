using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

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

    public async Task RemoveAllAsync(DocumentType documentType, AggregateContainerGroup aggregateContainerGroup)
    {
        await DbActionAsync(async (dbContext) => {
            switch (documentType, aggregateContainerGroup)
            {
                case (DocumentType.Event, AggregateContainerGroup.Default):
                    await dbContext.RemoveAllEventsAsync();
                    break;

                case (DocumentType.Event, AggregateContainerGroup.Dissolvable):
                    await dbContext.RemoveAllDissolvableEventsAsync();
                    break;

                case (DocumentType.Command, _):
                    await dbContext.RemoveAllCommandsAsync();
                    break;

                case (DocumentType.AggregateSnapshot, _):
                    await dbContext.RemoveAllSingleProjectionSnapshotsAsync();
                    break;

                case (DocumentType.MultiProjectionSnapshot, _):
                    await dbContext.RemoveAllMultiProjectionSnapshotsAsync();
                    break;

                default:
                    throw new NotImplementedException();
            }
        });
    }

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
