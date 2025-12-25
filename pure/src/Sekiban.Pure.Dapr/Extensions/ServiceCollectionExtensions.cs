using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.EventStore;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Dapr.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanWithDapr(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes,
        Action<DaprSekibanOptions>? configureOptions = null)
    {
        // Configure options
        services.Configure<DaprSekibanOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Add Dapr client directly
        services.AddSingleton<DaprClient>(provider =>
        {
            return new DaprClientBuilder().Build();
        });

        // Add serialization services
        services.AddSekibanDaprSerialization();

        // Add Actors - ensure they are properly registered with Dapr
        services.AddActors(options =>
        {
            // Register core Sekiban actors for standard Dapr usage
            options.Actors.RegisterActor<AggregateActor>();
            options.Actors.RegisterActor<AggregateEventHandlerActor>();
            options.Actors.RegisterActor<MultiProjectorActor>();

            // Configure actor runtime settings
            options.ActorIdleTimeout = TimeSpan.FromMinutes(30);
            options.ActorScanInterval = TimeSpan.FromSeconds(30);
        });

        // Register Sekiban services
        services.AddSingleton(domainTypes);

        // Register event storage services with new serialization
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<DaprClient>();
            var serialization = provider.GetRequiredService<IDaprSerializationService>();
            var logger = provider.GetRequiredService<ILogger<DaprEventStore>>();
            return new DaprEventStore(daprClient, serialization, logger);
        });
        services.AddScoped<ISekibanExecutor, SekibanDaprExecutor>();

        return services;
    }

    public static IServiceCollection AddSekibanDaprQueryService(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes)
    {
        services.AddSingleton(domainTypes);
        services.AddSingleton<DaprClient>(provider =>
        {
            return new DaprClientBuilder().Build();
        });
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<DaprClient>();
            var serialization = provider.GetRequiredService<IDaprSerializationService>();
            var logger = provider.GetRequiredService<ILogger<DaprEventStore>>();
            return new DaprEventStore(daprClient, serialization, logger);
        });

        return services;
    }

    /// <summary>
    ///     Adds Sekiban services for EventRelay pattern - does not register actors
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="domainTypes">The domain types to register</param>
    /// <param name="configureOptions">Options configuration</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSekibanWithDaprForEventRelay(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes,
        Action<DaprSekibanOptions>? configureOptions = null)
    {
        // Configure options
        services.Configure<DaprSekibanOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        // Add Dapr client directly
        services.AddSingleton<DaprClient>(provider =>
        {
            return new DaprClientBuilder().Build();
        });

        // Add serialization services
        services.AddSekibanDaprSerialization();

        // Register Sekiban domain types
        services.AddSingleton(domainTypes);

        // Add Actor proxy factory for forwarding events to actors in other services
        // This does NOT register actors in this service, only provides the proxy factory
        services.AddSingleton<IActorProxyFactory>(provider =>
        {
            return new ActorProxyFactory();
        });

        // Note: No actors registration for EventRelay
        // Note: No repository or executor registration
        // EventRelay only forwards events to existing actors in other services

        return services;
    }


    /// <summary>
    ///     Adds Dapr serialization services
    /// </summary>
    public static IServiceCollection AddSekibanDaprSerialization(
        this IServiceCollection services,
        Action<DaprSerializationOptions>? configure = null)
    {
        var options = new DaprSerializationOptions();
        configure?.Invoke(options);

        services.Configure<DaprSerializationOptions>(opt =>
        {
            opt.EnableCompression = options.EnableCompression;
            opt.EnableTypeAliases = options.EnableTypeAliases;
            opt.CompressionThreshold = options.CompressionThreshold;
            opt.CompressionLevel = options.CompressionLevel;
            opt.JsonSerializerOptions = options.JsonSerializerOptions;
        });

        services.AddSingleton<IDaprTypeRegistry, DaprTypeRegistry>();
        services.AddSingleton<IDaprSerializationService, DaprSerializationService>();
        services.AddSingleton<CachedDaprSerializationService>();

        return services;
    }
}
