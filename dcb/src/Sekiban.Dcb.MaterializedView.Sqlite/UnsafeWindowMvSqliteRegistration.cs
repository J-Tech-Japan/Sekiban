using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public static class UnsafeWindowMvSqliteRegistration
{
    public static string ConnectionStringKey<TProjector>() =>
        "UnsafeWindowMvSqlite:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static string SchemaResolverKey<TProjector>() =>
        "UnsafeWindowMvSqliteResolver:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvSqlite<TProjector, TRow>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbMaterializedViewSqlite",
        TimeSpan? idleDelay = null,
        int catchUpBatchSize = 256,
        int promotionBatchSize = 32)
        where TProjector : class, IUnsafeWindowMvProjector<TRow>
        where TRow : class, new()
    {
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            throw new ArgumentException("Connection string name must be non-empty.", nameof(connectionStringName));
        }

        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? configuration[$"ConnectionStrings:{connectionStringName}"]
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' not found — unsafe window MV (SQLite) requires a SQLite connection string.");

        return services.AddSekibanDcbUnsafeWindowMvSqlite<TProjector, TRow>(
            connectionString,
            idleDelay,
            catchUpBatchSize,
            promotionBatchSize);
    }

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvSqlite<TProjector, TRow>(
        this IServiceCollection services,
        string connectionString,
        TimeSpan? idleDelay = null,
        int catchUpBatchSize = 256,
        int promotionBatchSize = 32)
        where TProjector : class, IUnsafeWindowMvProjector<TRow>
        where TRow : class, new()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be non-empty.", nameof(connectionString));
        }

        services.AddSingleton<TProjector>();
        services.AddSingleton<IUnsafeWindowMvProjector<TRow>>(sp => sp.GetRequiredService<TProjector>());

        services.AddKeyedSingleton(SchemaResolverKey<TProjector>(), (sp, _) =>
        {
            var projector = sp.GetRequiredService<TProjector>();
            return new UnsafeWindowMvSqliteSchemaResolver(projector.ViewName, projector.ViewVersion, projector.Schema);
        });
        services.AddKeyedSingleton(ConnectionStringKey<TProjector>(), (_, _) => connectionString);

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqliteSchemaResolver>(SchemaResolverKey<TProjector>());
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqliteInitializer>>();
            return new UnsafeWindowMvSqliteInitializer(resolver, connectionString, logger);
        });

        services.AddSingleton<UnsafeWindowMvSqliteCatchUpWorker<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqliteSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqliteCatchUpWorker<TRow>>>();
            return new UnsafeWindowMvSqliteCatchUpWorker<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, catchUpBatchSize);
        });

        services.AddSingleton<UnsafeWindowMvSqlitePromoter<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqliteSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqlitePromoter<TRow>>>();
            return new UnsafeWindowMvSqlitePromoter<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, promotionBatchSize);
        });

        services.AddHostedService(sp =>
        {
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var initializer = sp.GetRequiredService<UnsafeWindowMvSqliteInitializer>();
            var catchUp = sp.GetRequiredService<UnsafeWindowMvSqliteCatchUpWorker<TRow>>();
            var promoter = sp.GetRequiredService<UnsafeWindowMvSqlitePromoter<TRow>>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvHostedService>>();
            return new UnsafeWindowMvHostedService(initializer, catchUp, promoter, projector.ViewName, logger, idleDelay);
        });

        return services;
    }
}
