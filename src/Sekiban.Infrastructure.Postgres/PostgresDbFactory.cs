using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Postgres.Databases;
namespace Sekiban.Infrastructure.Postgres;

public class PostgresDbFactory(
    SekibanPostgresOptions dbOptions,
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider)
{
    private string SekibanContextIdentifier()
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
    private SekibanPostgresDbOption GetSekibanDbOption()
    {
        return dbOptions.Contexts.Find(m => m.Context == SekibanContextIdentifier()) ?? new SekibanPostgresDbOption();
    }

    private string GetTableId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        return (documentType, containerGroup) switch
        {
            (DocumentType.Event, AggregateContainerGroup.Default) => SekibanPostgresDbOption.EventsTableId,
            (DocumentType.Event, AggregateContainerGroup.Dissolvable) => SekibanPostgresDbOption
                .EventsTableIdDissolvable,
            (_, AggregateContainerGroup.Default) => SekibanPostgresDbOption.ItemsTableId,
            _ => SekibanPostgresDbOption.ItemsTableIdDissolvable
        };
    }

    private static string GetMemoryCacheDbContextKey(string sekibanContextIdentifier) =>
        $"dbContext.{sekibanContextIdentifier}";
    private string GetConnectionString()
    {
        var dbOption = GetSekibanDbOption();
        return dbOption.ConnectionString ?? string.Empty;
    }
    private bool GetMigrationFinished()
    {
        var dbOption = GetSekibanDbOption();
        return dbOption.MigrationFinished;
    }
    private void SetMigrationFinished()
    {
        var dbOption = GetSekibanDbOption();
        dbOption.MigrationFinished = true;
    }
    private async Task<SekibanDbContext> GetDbContextAsync()
    {
        // var dbContextFromCache = (SekibanDbContext?)memoryCache.Cache.Get(GetMemoryCacheDbContextKey(SekibanContextIdentifier()));
        //
        // if (dbContextFromCache is not null)
        // {
        //     return dbContextFromCache;
        // }
        //
        var connectionString = GetConnectionString();
        var dbContext = new SekibanDbContext(new DbContextOptions<SekibanDbContext>())
            { ConnectionString = connectionString };
        if (!GetMigrationFinished())
        {
            await dbContext.Database.MigrateAsync();
            SetMigrationFinished();
        }
        // memoryCache.Cache.Set(GetMemoryCacheDbContextKey(SekibanContextIdentifier()), dbContext, new MemoryCacheEntryOptions());
        await Task.CompletedTask;
        return dbContext;
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(
        DocumentType documentType,
        AggregateContainerGroup containerGroup = AggregateContainerGroup.Default)
    {
        await DbActionAsync(
            async dbContext =>
            {
                switch (documentType, containerGroup)
                {
                    case (DocumentType.Event, AggregateContainerGroup.Default):
                        dbContext.Events.RemoveRange(dbContext.Events);
                        break;
                    case (DocumentType.Command, _):
                        dbContext.Commands.RemoveRange(
                            dbContext.Commands.Where(m => m.AggregateContainerGroup == containerGroup));
                        break;
                    case (DocumentType.AggregateSnapshot, _):
                        dbContext.SingleProjectionSnapshots.RemoveRange(
                            dbContext.SingleProjectionSnapshots.Where(
                                m => m.AggregateContainerGroup == containerGroup));
                        break;
                    case (DocumentType.MultiProjectionSnapshot, _):
                        dbContext.MultiProjectionSnapshots.RemoveRange(
                            dbContext.MultiProjectionSnapshots.Where(m => m.AggregateContainerGroup == containerGroup));
                        break;
                    case (DocumentType.Event, AggregateContainerGroup.Dissolvable):
                        dbContext.DissolvableEvents.RemoveRange(dbContext.DissolvableEvents);
                        break;
                }
                await dbContext.SaveChangesAsync();
            });
    }
    public async Task DeleteAllFromEventContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Event, containerGroup);
    }
    public async Task DeleteAllFromItemsContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, containerGroup);
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateSnapshot, containerGroup);
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.MultiProjectionSnapshot, containerGroup);
    }
    private void ResetMemoryCache()
    {
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        memoryCache.Cache.Remove(GetMemoryCacheDbContextKey(SekibanContextIdentifier()));
    }
    public async Task<T> DbActionAsync<T>(Func<SekibanDbContext, Task<T>> dbAction)
    {
        try
        {
            await using var dbContext = await GetDbContextAsync();
            var result = await dbAction(dbContext);
            return result;
        }
        catch
        {
            ResetMemoryCache();
            throw;
        }
    }
    public async Task DbActionAsync(Func<SekibanDbContext, Task> dbAction)
    {
        try
        {
            await using var dbContext = await GetDbContextAsync();
            await dbAction(dbContext);
        }
        catch
        {
            // There may be a network error, so initialize the container.
            // This allows reconnection when recovered next time.
            ResetMemoryCache();
            throw;
        }
    }
}
