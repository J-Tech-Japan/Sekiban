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
        where TRow : class
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
        services.AddSingleton(sp =>
        {
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            return new UnsafeWindowMvSchemaResolver(projector.ViewName, projector.ViewVersion, projector.Schema);
        });

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredService<UnsafeWindowMvSchemaResolver>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvInitializer>>();
            return new UnsafeWindowMvInitializer(resolver, connectionString, logger);
        });

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredService<UnsafeWindowMvSchemaResolver>();
            var projector = sp.GetRequiredService<IUnsafeWindowMvProjector<TRow>>();
            var eventStore = sp.GetRequiredService<IEventStore>();
            var eventTypes = sp.GetRequiredService<IEventTypes>();
            var logger = sp.GetRequiredService<ILogger<UnsafeWindowMvCatchUpWorker<TRow>>>();
            return new UnsafeWindowMvCatchUpWorker<TRow>(resolver, projector, eventStore, eventTypes, connectionString, logger, catchUpBatchSize);
        });

        services.AddSingleton(sp =>
        {
            var resolver = sp.GetRequiredService<UnsafeWindowMvSchemaResolver>();
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

        // Connection string exposed via a keyed singleton so samples / tests
        // (e.g. a direct `/api/uwmv/weather` endpoint) can read rows from
        // `current_live` without re-resolving it from configuration.
        services.AddKeyedSingleton("UnsafeWindowMv:" + typeof(TRow).FullName, (_, _) => connectionString);
        return services;
    }
}
