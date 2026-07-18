using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.CosmosDb.Migration;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Service collection extensions for CosmosDB-backed DCB.
/// </summary>
public static class SekibanDcbCosmosDbExtensions
{
    private const string DefaultDatabaseName = "SekibanDcb";
    private static readonly Action<ILogger, string, Exception?> LogUsingAspireClient =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(LogUsingAspireClient)), "Using Aspire-provided CosmosClient for database {DatabaseName}");

    private static readonly Action<ILogger, string, Exception?> LogUsingConnectionString =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(LogUsingConnectionString)), "Using connection string for CosmosDB database {DatabaseName}");

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage (single-tenant default).
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDb(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CosmosDbEventStoreOptions>? configureOptions = null)
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();

        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? DefaultDatabaseName;

            // Check for connection strings in order of specificity
            var connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
                ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
                ?? configuration.GetConnectionString("CosmosDb")
                ?? configuration.GetConnectionString("cosmosdb");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "No CosmosDB connection string found. Configure a connection string in " +
                    "'ConnectionStrings:SekibanDcbCosmos', 'ConnectionStrings:SekibanDcbCosmosDb', " +
                    "'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
            }

            return new CosmosDbContext(connectionString, databaseName, logger, options);
        });

        // Register store implementations
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddSingleton<CosmosDbEventStore>();
        services.AddSingleton<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Storage.IConditionalEventStore>(sp => (Sekiban.Dcb.Storage.IConditionalEventStore)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider>(sp => (Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage using connection string (single-tenant default).
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDb(
        this IServiceCollection services,
        string connectionString,
        string databaseName = DefaultDatabaseName,
        Action<CosmosDbEventStoreOptions>? configureOptions = null)
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();

        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            return new CosmosDbContext(connectionString, databaseName, logger, options);
        });

        // Register store implementations
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddSingleton<CosmosDbEventStore>();
        services.AddSingleton<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Storage.IConditionalEventStore>(sp => (Sekiban.Dcb.Storage.IConditionalEventStore)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider>(sp => (Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage for Aspire (single-tenant default).
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbWithAspire(
        this IServiceCollection services,
        Action<CosmosDbEventStoreOptions>? configureOptions = null)
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();

        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? DefaultDatabaseName;

            // Try to get CosmosClient from Aspire DI first (if Aspire has registered it)
            var cosmosClient = provider.GetService<Microsoft.Azure.Cosmos.CosmosClient>();

            if (cosmosClient != null)
            {
                // Use Aspire-provided CosmosClient
                if (logger != null)
                {
                    LogUsingAspireClient(logger, databaseName, null);
                }
                return new CosmosDbContext(cosmosClient, databaseName, logger, options);
            }

            // Fall back to connection string if no Aspire client is available
            // Check for connection strings in order of specificity
            var connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
                ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
                ?? configuration.GetConnectionString("CosmosDb")
                ?? configuration.GetConnectionString("cosmosdb");

            if (!string.IsNullOrEmpty(connectionString))
            {
                if (logger != null)
                {
                    LogUsingConnectionString(logger, databaseName, null);
                }
                return new CosmosDbContext(connectionString, databaseName, logger, options);
            }

            throw new InvalidOperationException(
                "No CosmosDB configuration found. Either provide a CosmosClient through Aspire, " +
                "or configure a connection string in 'ConnectionStrings:SekibanDcbCosmos', " +
                "'ConnectionStrings:SekibanDcbCosmosDb', 'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
        });

        // Register store implementations
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddSingleton<CosmosDbEventStore>();
        services.AddSingleton<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Storage.IConditionalEventStore>(sp => (Sekiban.Dcb.Storage.IConditionalEventStore)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider>(sp => (Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        // Register hosted service to ensure containers are created
        services.AddHostedService<CosmosDbInitializer>();

        return services;
    }

    /// <summary>
    ///     Registers CosmosDB services for multi-tenant HTTP API deployment.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbMultiTenant(
        this IServiceCollection services,
        Action<CosmosDbEventStoreOptions>? configureOptions = null,
        string serviceIdClaimType = "service_id")
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();

        services.AddHttpContextAccessor();
        services.AddScoped<IServiceIdProvider>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            return new JwtServiceIdProvider(accessor, serviceIdClaimType);
        });

        AddCosmosDbContext(services, options);
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddScoped<CosmosDbEventStore>();
        services.AddScoped<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddScoped<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddScoped<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        return services;
    }

    /// <summary>
    ///     Registers CosmosDB services with factories for Orleans grains.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbWithFactories(
        this IServiceCollection services,
        Action<CosmosDbEventStoreOptions>? configureOptions = null)
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        AddCosmosDbContext(services, options);

        services.AddSingleton<IEventStoreFactory, CosmosDbEventStoreFactory>();
        services.AddSingleton<IMultiProjectionStateStoreFactory, CosmosDbMultiProjectionStateStoreFactory>();

        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddSingleton<CosmosDbEventStore>();
        services.AddSingleton<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Storage.IConditionalEventStore>(sp => (Sekiban.Dcb.Storage.IConditionalEventStore)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider>(sp => (Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        return services;
    }

    /// <summary>
    ///     Registers CosmosDB services for combined HTTP API and Orleans deployment.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbFull(
        this IServiceCollection services,
        Action<CosmosDbEventStoreOptions>? configureOptions = null,
        string serviceIdClaimType = "service_id")
    {
        var options = new CosmosDbEventStoreOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICosmosContainerResolver, DefaultCosmosContainerResolver>();
        AddCosmosDbContext(services, options);

        services.AddHttpContextAccessor();
        services.AddScoped<IServiceIdProvider>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            if (accessor.HttpContext != null)
            {
                return new JwtServiceIdProvider(accessor, serviceIdClaimType);
            }

            return new RequiredServiceIdProvider();
        });

        services.AddSingleton<IEventStoreFactory, CosmosDbEventStoreFactory>();
        services.AddSingleton<IMultiProjectionStateStoreFactory, CosmosDbMultiProjectionStateStoreFactory>();

        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.AddScoped<CosmosDbEventStore>();
        services.AddScoped<IHotEventStore>(sp => sp.GetRequiredService<CosmosDbEventStore>());
        services.AddScoped<IEventStore>(sp => sp.GetRequiredService<IHotEventStore>());
        services.AddScoped<IMultiProjectionStateStore, CosmosMultiProjectionStateStore>();

        return services;
    }

    /// <summary>
    ///     Registers the administrative tag-repair factory (<see cref="CosmosDbTagRepairServiceFactory" />)
    ///     alongside an existing Cosmos registration.
    ///     Kept out of <c>AddSekibanDcbCosmosDb</c> deliberately: repair is an operator tool, so an
    ///     application only gets it if it asks. Resolve the factory in an operator-only job and create a
    ///     service for the one service id you intend to repair — never expose it from a request path.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbTagRepair(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CosmosDbTagRepairServiceFactory>();
        return services;
    }

    /// <summary>
    ///     Registers the automatic tag-index sweep, which runs the non-destructive repair over a recent
    ///     window shortly after startup and optionally on an interval.
    ///     Opt-in twice over: this method is not called by <c>AddSekibanDcbCosmosDb</c>, and the sweep stays
    ///     inert until <see cref="CosmosTagSweepOptions.Enabled" /> is set — so neither referencing the
    ///     package nor upgrading it can start scanning anyone's containers.
    ///     The sweep is EVENTUAL repair. It does not gate tag readers, and a missing-tag window stays open
    ///     until a run reaches it. See the storage-provider docs before relying on it.
    ///     Measure the RU cost with a manual dry run before enabling an interval.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbTagSweep(
        this IServiceCollection services,
        Action<CosmosTagSweepOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sweepOptions = new CosmosTagSweepOptions();
        configureOptions?.Invoke(sweepOptions);

        services.AddSekibanDcbCosmosDbTagRepair();
        services.AddSingleton(sweepOptions);
        services.AddHostedService(provider =>
        {
            var factory = provider.GetRequiredService<CosmosDbTagRepairServiceFactory>();

            // Empty means the host's own lineage — the right default for a single-tenant deployment.
            var serviceIds = sweepOptions.ServiceIds.Count > 0
                ? sweepOptions.ServiceIds.ToList()
                : new List<string> { provider.GetRequiredService<IServiceIdProvider>().GetCurrentServiceId() };

            return new CosmosTagSweepService(
                sweepOptions,
                serviceIds,
                new CosmosTagRepairRunner(factory),
                logger: provider.GetService<ILogger<CosmosTagSweepService>>());
        });

        return services;
    }

    /// <summary>
    ///     Registers the DESTRUCTIVE legacy tag-row migration factory
    ///     (<see cref="CosmosDbLegacyTagMigrationServiceFactory" />).
    ///     This is the only surface in the provider that can delete a tag row. It is not registered by
    ///     <c>AddSekibanDcbCosmosDb</c>, it is not reachable from the automatic sweep, and even once
    ///     registered it does nothing until an operator plans a migration, reads the artifact, confirms, and
    ///     supplies a backup writer.
    ///     Migration is optional: legacy rows index their events perfectly well and the repair service
    ///     recognizes them. Register this only when an operator is going to tidy them up on purpose.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbLegacyTagMigration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<CosmosDbLegacyTagMigrationServiceFactory>();
        return services;
    }

    private static void AddCosmosDbContext(IServiceCollection services, CosmosDbEventStoreOptions options)
    {
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "SekibanDcb";

            var connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
                ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
                ?? configuration.GetConnectionString("CosmosDb")
                ?? configuration.GetConnectionString("cosmosdb");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "No CosmosDB connection string found. Configure a connection string in " +
                    "'ConnectionStrings:SekibanDcbCosmos', 'ConnectionStrings:SekibanDcbCosmosDb', " +
                    "'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
            }

            return new CosmosDbContext(connectionString, databaseName, logger, options);
        });
    }
}
