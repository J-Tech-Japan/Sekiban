using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
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
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddSingleton<IEventStore>(sp =>
        {
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetService<ILogger<SqliteEventStore>>();
            var serviceIdProvider = sp.GetRequiredService<IServiceIdProvider>();
            return new SqliteEventStore(databasePath, eventTypes, options, logger, serviceIdProvider);
        });

        services.AddSingleton<IMultiProjectionStateStore>(sp =>
        {
            var logger = sp.GetService<ILogger<SqliteMultiProjectionStateStore>>();
            var serviceIdProvider = sp.GetRequiredService<IServiceIdProvider>();
            return new SqliteMultiProjectionStateStore(databasePath, logger, serviceIdProvider);
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
        services.TryAddSingleton<ITagTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagTypes);
        services.TryAddSingleton<ITagProjectorTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagProjectorTypes);
        services.TryAddSingleton(sp => sp.GetRequiredService<DcbDomainTypes>().JsonSerializerOptions);
        services.AddSingleton<TagEventService>();
        services.AddSingleton<TagStateService>();
        services.AddSingleton<TagListService>();
        return services;
    }

    /// <summary>
    ///     Create a SqliteEventStore instance for use as a local cache
    /// </summary>
    /// <param name="databasePath">Path to the SQLite database file</param>
    /// <param name="eventTypes">Event types for serialization</param>
    /// <param name="options">Optional configuration</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>SqliteEventStore instance</returns>
    public static SqliteEventStore CreateSqliteCache(
        string databasePath,
        IEventTypes eventTypes,
        SqliteEventStoreOptions? options = null,
        ILogger<SqliteEventStore>? logger = null)
    {
        return new SqliteEventStore(databasePath, eventTypes, options, logger);
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
