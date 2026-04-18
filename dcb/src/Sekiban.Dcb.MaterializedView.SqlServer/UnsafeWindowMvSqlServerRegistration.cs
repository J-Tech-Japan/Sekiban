using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

public static class UnsafeWindowMvSqlServerRegistration
{
    public static string ConnectionStringKey<TProjector>() =>
        "UnsafeWindowMvSqlServer:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static string SchemaResolverKey<TProjector>() =>
        "UnsafeWindowMvSqlServerResolver:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvSqlServer<TProjector, TRow>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbMaterializedViewSqlServer",
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
                $"Connection string '{connectionStringName}' not found — unsafe window MV (SQL Server) requires a SQL Server database.");

        return services.AddSekibanDcbUnsafeWindowMvSqlServer<TProjector, TRow>(
            connectionString,
            idleDelay,
            catchUpBatchSize,
            promotionBatchSize);
    }

    public static IServiceCollection AddSekibanDcbUnsafeWindowMvSqlServer<TProjector, TRow>(
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
            return new UnsafeWindowMvSqlServerSchemaResolver(projector.ViewName, projector.ViewVersion, projector.Schema);
        });
        services.AddKeyedSingleton(ConnectionStringKey<TProjector>(), (_, _) => connectionString);

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqlServerSchemaResolver>(SchemaResolverKey<TProjector>());
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqlServerInitializer>>();
            return new UnsafeWindowMvSqlServerInitializer(resolver, connectionString, logger);
        });

        services.AddSingleton<UnsafeWindowMvSqlServerCatchUpWorker<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqlServerSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqlServerCatchUpWorker<TRow>>>();
            return new UnsafeWindowMvSqlServerCatchUpWorker<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, catchUpBatchSize);
        });

        services.AddSingleton<UnsafeWindowMvSqlServerPromoter<TRow>>(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSqlServerSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvSqlServerPromoter<TRow>>>();
            return new UnsafeWindowMvSqlServerPromoter<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, promotionBatchSize);
        });

        services.AddHostedService(sp =>
        {
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var initializer = sp.GetRequiredService<UnsafeWindowMvSqlServerInitializer>();
            var catchUp = sp.GetRequiredService<UnsafeWindowMvSqlServerCatchUpWorker<TRow>>();
            var promoter = sp.GetRequiredService<UnsafeWindowMvSqlServerPromoter<TRow>>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvHostedService>>();
            return new UnsafeWindowMvHostedService(initializer, catchUp, promoter, projector.ViewName, logger, idleDelay);
        });

        return services;
    }
}
