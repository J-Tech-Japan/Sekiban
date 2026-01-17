using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Sqlite.Services;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Extension methods for registering Sekiban DCB SQLite services
/// </summary>
public static class SekibanDcbSqliteExtensions
{
    /// <summary>
    ///     Add SQLite event store as the primary event store
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="databasePath">Path to the SQLite database file</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddSekibanDcbSqlite(
        this IServiceCollection services,
        string databasePath,
        Action<SqliteEventStoreOptions>? configure = null)
    {
        var options = new SqliteEventStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IEventStore>(sp =>
        {
            var domainTypes = sp.GetRequiredService<DcbDomainTypes>();
            var logger = sp.GetService<ILogger<SqliteEventStore>>();
            return new SqliteEventStore(databasePath, domainTypes, options, logger);
        });

        services.AddSingleton<IMultiProjectionStateStore>(sp =>
        {
            var logger = sp.GetService<ILogger<SqliteMultiProjectionStateStore>>();
            return new SqliteMultiProjectionStateStore(databasePath, logger);
        });

        return services;
    }

    /// <summary>
    ///     Add CLI services for tag operations
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddSekibanDcbCliServices(this IServiceCollection services)
    {
        services.AddSingleton<TagEventService>();
        services.AddSingleton<TagStateService>();
        services.AddSingleton<TagListService>();
        return services;
    }

    /// <summary>
    ///     Create a SqliteEventStore instance for use as a local cache
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file</param>
    /// <param name="domainTypes">Domain types for serialization</param>
    /// <param name="options">Optional configuration</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>SqliteEventStore instance</returns>
    public static SqliteEventStore CreateSqliteCache(
        string databasePath,
        DcbDomainTypes domainTypes,
        SqliteEventStoreOptions? options = null,
        ILogger<SqliteEventStore>? logger = null)
    {
        return new SqliteEventStore(databasePath, domainTypes, options, logger);
    }

    /// <summary>
    ///     Create a cache sync helper for synchronizing a remote store to local SQLite cache
    /// </summary>
    /// <param name="localStore">Local SQLite event store</param>
    /// <param name="remoteStore">Remote event store (Cosmos, Postgres, etc.)</param>
    /// <param name="options">Sync options</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>EventStoreCacheSync instance</returns>
    public static EventStoreCacheSync CreateCacheSync(
        SqliteEventStore localStore,
        IEventStore remoteStore,
        CacheSyncOptions? options = null,
        ILogger<EventStoreCacheSync>? logger = null)
    {
        return new EventStoreCacheSync(localStore, remoteStore, options, logger);
    }
}
