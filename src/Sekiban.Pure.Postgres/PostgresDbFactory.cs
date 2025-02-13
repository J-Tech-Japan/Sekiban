using Microsoft.EntityFrameworkCore;
namespace Sekiban.Pure.Postgres;

public class PostgresDbFactory(
    SekibanPostgresDbOption dbOption,
    IPostgresMemoryCacheAccessor memoryCache)
{
    private static string GetMemoryCacheDbContextKey() => "dbContext.Postgres";

    private string GetConnectionString() => dbOption.ConnectionString ?? string.Empty;

    private bool GetMigrationFinished() => dbOption.MigrationFinished;

    private void SetMigrationFinished()
    {
        dbOption.MigrationFinished = true;
    }

    private async Task<SekibanDbContext> GetDbContextAsync()
    {
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

    public async Task DeleteAllFromAggregateFromContainerIncludes()
    {
        await DbActionAsync(
            async dbContext =>
            {
                dbContext.Events.RemoveRange(dbContext.Events);
                await dbContext.SaveChangesAsync();
            });
    }

    public async Task DeleteAllFromEventContainer()
    {
        await DeleteAllFromAggregateFromContainerIncludes();
    }

    private void ResetMemoryCache()
    {
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        memoryCache.Cache.Remove(GetMemoryCacheDbContextKey());
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