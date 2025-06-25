using Dapr.Actors.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Services;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
using Sekiban.Pure;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.EventStore;
using Sekiban.Pure.Dapr.Serialization;

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
        services.AddSingleton<global::Dapr.Client.DaprClient>(provider =>
        {
            return new global::Dapr.Client.DaprClientBuilder().Build();
        });
        
        // Add Actors
        services.AddActors(options =>
        {
            options.Actors.RegisterActor<AggregateActor>();
            options.Actors.RegisterActor<AggregateEventHandlerActor>();
            options.Actors.RegisterActor<MultiProjectorActor>();
            
            options.ActorIdleTimeout = TimeSpan.FromMinutes(30);
            options.ActorScanInterval = TimeSpan.FromSeconds(30);
        });

        // Register Sekiban services
        services.AddSingleton(domainTypes);
        
        // Register event storage services with new serialization
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<global::Dapr.Client.DaprClient>();
            var serialization = provider.GetRequiredService<IDaprSerializationService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sekiban.Pure.Dapr.EventStore.DaprEventStore>>();
            return new Sekiban.Pure.Dapr.EventStore.DaprEventStore(daprClient, serialization, logger);
        });
        services.AddSingleton<IEventWriter>(provider => (IEventWriter)provider.GetRequiredService<Repository>());
        services.AddSingleton<IEventReader>(provider => (IEventReader)provider.GetRequiredService<Repository>());
        
        services.AddScoped<ISekibanExecutor, SekibanDaprExecutor>();

        return services;
    }

    public static IServiceCollection AddSekibanDaprQueryService(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes)
    {
        services.AddSingleton(domainTypes);
        services.AddSingleton<global::Dapr.Client.DaprClient>(provider =>
        {
            return new global::Dapr.Client.DaprClientBuilder().Build();
        });
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<global::Dapr.Client.DaprClient>();
            var serialization = provider.GetRequiredService<IDaprSerializationService>();
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Sekiban.Pure.Dapr.EventStore.DaprEventStore>>();
            return new Sekiban.Pure.Dapr.EventStore.DaprEventStore(daprClient, serialization, logger);
        });
        
        return services;
    }
}