using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public static class UnsafeWindowMvRegistration
{
    /// <summary>
    ///     Returns the keyed-singleton lookup name sample / test code can use
    ///     to resolve the Postgres connection string that the runtime wired
    ///     for the projector <typeparamref name="TProjector" />. Keyed by the
    ///     projector type so multiple projectors (including v1/v2 side-by-side
    ///     or projectors that share a common row DTO) register without
    ///     colliding.
    /// </summary>
    public static string ConnectionStringKey<TProjector>() =>
        "UnsafeWindowMv:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    /// <summary>
    ///     Returns the keyed-singleton lookup name sample / test code can use
    ///     to resolve the <see cref="UnsafeWindowMvSchemaResolver" /> wired
    ///     for the projector <typeparamref name="TProjector" />. Using the
    ///     projector type as the key avoids collisions when several views
    ///     share a single row DTO.
    /// </summary>
    public static string SchemaResolverKey<TProjector>() =>
        "UnsafeWindowMvResolver:" + (typeof(TProjector).FullName ?? typeof(TProjector).Name);

    /// <summary>
    ///     Registers an unsafe-window materialized view projector and all the
    ///     supporting runtime pieces (initializer, catch-up worker, promoter,
    ///     hosted service). Pulls the Postgres connection string from
    ///     <paramref name="configuration" /> using <paramref name="connectionStringName" />.
    /// </summary>
    public static IServiceCollection AddSekibanDcbUnsafeWindowMv<TProjector, TRow>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbMaterializedViewPostgres",
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
                $"Connection string '{connectionStringName}' not found — unsafe window MV requires a Postgres database.");

        services.AddSingleton<TProjector>();
        services.AddSingleton<IUnsafeWindowMvProjector<TRow>>(sp => sp.GetRequiredService<TProjector>());

        // Keyed by TProjector so multiple projectors can coexist even if they
        // share a TRow DTO. Callers resolve via `ConnectionStringKey<TProjector>()`
        // and `SchemaResolverKey<TProjector>()`.
        services.AddKeyedSingleton(SchemaResolverKey<TProjector>(), (sp, _) =>
        {
            var projector = sp.GetRequiredService<TProjector>();
            return new UnsafeWindowMvSchemaResolver(projector.ViewName, projector.ViewVersion, projector.Schema);
        });
        services.AddKeyedSingleton(ConnectionStringKey<TProjector>(), (_, _) => connectionString);

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSchemaResolver>(SchemaResolverKey<TProjector>());
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvInitializer>>();
            return new UnsafeWindowMvInitializer(resolver, connectionString, logger);
        });

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvCatchUpWorker<TRow>>>();
            return new UnsafeWindowMvCatchUpWorker<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, catchUpBatchSize);
        });

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredKeyedService<UnsafeWindowMvSchemaResolver>(SchemaResolverKey<TProjector>());
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvPromoter<TRow>>>();
            return new UnsafeWindowMvPromoter<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, promotionBatchSize);
        });

        services.AddHostedService(sp =>
        {
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var initializer = sp.GetRequiredService<UnsafeWindowMvInitializer>();
            var catchUp = sp.GetRequiredService<UnsafeWindowMvCatchUpWorker<TRow>>();
            var promoter = sp.GetRequiredService<UnsafeWindowMvPromoter<TRow>>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvHostedService<TRow>>>();
            return new UnsafeWindowMvHostedService<TRow>(initializer, catchUp, promoter, projector.ViewName, logger, idleDelay);
        });

        return services;
    }
}
