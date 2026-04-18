using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.MySql;

public static class UnsafeWindowMvMySqlRegistration
{
    public static string ConnectionStringKey<TProjector>() =>
        "UnsafeWindowMvMySql:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static string SchemaResolverKey<TProjector>() =>
        "UnsafeWindowMvMySqlResolver:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvMySql<TProjector, TRow>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbMaterializedViewMySql",
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
                $"Connection string '{connectionStringName}' not found — unsafe window MV (MySQL) requires a MySQL database.");

        return services.AddSekibanDcbUnsafeWindowMvMySql<TProjector, TRow>(
            connectionString,
            idleDelay,
            catchUpBatchSize,
            promotionBatchSize);
    }

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvMySql<TProjector, TRow>(
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
            return new UnsafeWindowMvMySqlSchemaResolver(projector.ViewName, projector.ViewVersion, projector.Schema);
        });
        services.AddKeyedSingleton(ConnectionStringKey<TProjector>(), (_, _) => connectionString);

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvMySqlSchemaResolver>(SchemaResolverKey<TProjector>());
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvMySqlInitializer>>();
            return new UnsafeWindowMvMySqlInitializer(resolver, connectionString, logger);
        });

        services.AddSingleton<UnsafeWindowMvMySqlCatchUpWorker<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvMySqlSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvMySqlCatchUpWorker<TRow>>>();
            return new UnsafeWindowMvMySqlCatchUpWorker<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, catchUpBatchSize);
        });

        services.AddSingleton<UnsafeWindowMvMySqlPromoter<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvMySqlSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvMySqlPromoter<TRow>>>();
            return new UnsafeWindowMvMySqlPromoter<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, promotionBatchSize);
        });

        services.AddHostedService(sp =>
        {
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var initializer = sp.GetRequiredService<UnsafeWindowMvMySqlInitializer>();
            var catchUp = sp.GetRequiredService<UnsafeWindowMvMySqlCatchUpWorker<TRow>>();
            var promoter = sp.GetRequiredService<UnsafeWindowMvMySqlPromoter<TRow>>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvHostedService>>();
            return new UnsafeWindowMvHostedService(initializer, catchUp, promoter, projector.ViewName, logger, idleDelay);
        });

        return services;
    }
}
