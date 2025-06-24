using Dapr.Actors.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Services;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
using Sekiban.Pure;

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

        // Add Dapr (will be added when configuring MVC/WebAPI)
        
        // Add Actors
        services.AddActors(options =>
        {
            options.Actors.RegisterActor<AggregateActor>();
            options.Actors.RegisterActor<MultiProjectorActor>();
            
            options.ActorIdleTimeout = TimeSpan.FromMinutes(30);
            options.ActorScanInterval = TimeSpan.FromSeconds(30);
        });

        // Register Sekiban services
        services.AddSingleton(domainTypes);
        services.AddSingleton<Repository, DaprEventStore>();
        services.AddScoped<ISekibanExecutor, SekibanDaprExecutor>();

        return services;
    }

    public static IServiceCollection AddSekibanDaprQueryService(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes)
    {
        services.AddSingleton(domainTypes);
        services.AddSingleton<Repository, DaprEventStore>();
        
        return services;
    }
}