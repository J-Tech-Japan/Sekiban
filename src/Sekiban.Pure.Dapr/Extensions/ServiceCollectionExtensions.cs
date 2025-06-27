using Dapr.Actors.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Adds Sekiban with Dapr using Protobuf serialization
    /// </summary>
    public static IServiceCollection AddSekibanWithDaprProtobuf(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes,
        Action<DaprSekibanOptions>? configureOptions = null,
        Action<DaprSerializationOptions>? configureSerializationOptions = null)
    {
        // Configure options
        services.Configure<DaprSekibanOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        services.Configure<DaprSerializationOptions>(options =>
        {
            configureSerializationOptions?.Invoke(options);
        });

        // Add Dapr client
        services.AddSingleton<global::Dapr.Client.DaprClient>(provider =>
        {
            return new global::Dapr.Client.DaprClientBuilder().Build();
        });
        
        // Add Protobuf serialization services
        services.AddSekibanDaprProtobufSerialization(configureSerializationOptions);
        
        // Note: AddActors is called only once in AddSekibanWithDapr to avoid conflicts
        // Protobuf actors are already registered in the main AddSekibanWithDapr method

        // Register Sekiban services
        services.AddSingleton(domainTypes);
        
        // Register Protobuf event storage
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<global::Dapr.Client.DaprClient>();
            var serialization = provider.GetRequiredService<IDaprProtobufSerializationService>();
            var logger = provider.GetRequiredService<ILogger<ProtobufDaprEventStore>>();
            return new ProtobufDaprEventStore(daprClient, serialization, logger);
        });
        services.AddSingleton<IEventWriter>(provider => (IEventWriter)provider.GetRequiredService<Repository>());
        services.AddSingleton<IEventReader>(provider => (IEventReader)provider.GetRequiredService<Repository>());
        
        // Register Protobuf executor
        services.AddScoped<ISekibanExecutor, SekibanProtobufDaprExecutor>();

        return services;
    }

    /// <summary>
    /// Adds Dapr serialization services
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

    /// <summary>
    /// Adds Dapr Protobuf serialization services
    /// </summary>
    public static IServiceCollection AddSekibanDaprProtobufSerialization(
        this IServiceCollection services,
        Action<DaprSerializationOptions>? configure = null)
    {
        var options = new DaprSerializationOptions();
        configure?.Invoke(options);
        
        // Configure JSON options for internal serialization
        if (options.JsonSerializerOptions == null)
        {
            options.JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }
        
        services.Configure<DaprSerializationOptions>(opt =>
        {
            opt.EnableCompression = options.EnableCompression;
            opt.EnableTypeAliases = options.EnableTypeAliases;
            opt.CompressionThreshold = options.CompressionThreshold;
            opt.CompressionLevel = options.CompressionLevel;
            opt.JsonSerializerOptions = options.JsonSerializerOptions;
        });
        
        services.AddSingleton<IDaprTypeRegistry, DaprTypeRegistry>();
        services.AddSingleton<IDaprProtobufSerializationService, DaprProtobufSerializationService>();
        services.AddSingleton<IDaprSerializationService>(provider => 
            provider.GetRequiredService<IDaprProtobufSerializationService>());
        
        // Register type registrations (this would be done by source generator in production)
        services.AddSingleton(provider =>
        {
            var registry = provider.GetRequiredService<IDaprTypeRegistry>();
            // In production, this would call the generated registration method:
            // DaprGeneratedTypeRegistry.RegisterAll(registry);
            return registry;
        });
        
        return services;
    }

    /// <summary>
    /// Adds Sekiban with Dapr using envelope-based actor communication
    /// This is the recommended approach for production use
    /// </summary>
    public static IServiceCollection AddSekibanWithDaprEnvelopes(
        this IServiceCollection services,
        SekibanDomainTypes domainTypes,
        Action<DaprSekibanOptions>? configureOptions = null,
        Action<DaprSerializationOptions>? configureSerializationOptions = null)
    {
        // Configure options
        services.Configure<DaprSekibanOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        services.Configure<DaprSerializationOptions>(options =>
        {
            configureSerializationOptions?.Invoke(options);
        });

        // Add Dapr client
        services.AddSingleton<global::Dapr.Client.DaprClient>(provider =>
        {
            return new global::Dapr.Client.DaprClientBuilder().Build();
        });
        
        // Add Protobuf serialization services
        services.AddSekibanDaprProtobufSerialization(configureSerializationOptions);
        
        // Add envelope services
        services.AddSingleton<IProtobufTypeMapper, ProtobufTypeMapper>();
        services.AddSingleton<IEnvelopeProtobufService, EnvelopeProtobufService>();
        
        // Note: AddActors is called only once in AddSekibanWithDapr to avoid conflicts
        // Envelope actors are already registered in the main AddSekibanWithDapr method

        // Register Sekiban services
        services.AddSingleton(domainTypes);
        
        // Register envelope-based event storage
        services.AddSingleton<Repository>(provider =>
        {
            var daprClient = provider.GetRequiredService<global::Dapr.Client.DaprClient>();
            var serialization = provider.GetRequiredService<IDaprProtobufSerializationService>();
            var logger = provider.GetRequiredService<ILogger<ProtobufDaprEventStore>>();
            return new ProtobufDaprEventStore(daprClient, serialization, logger);
        });
        services.AddSingleton<IEventWriter>(provider => (IEventWriter)provider.GetRequiredService<Repository>());
        services.AddSingleton<IEventReader>(provider => (IEventReader)provider.GetRequiredService<Repository>());
        
        // Register envelope-based executor
        services.AddScoped<ISekibanExecutor, SekibanEnvelopeDaprExecutor>();

        return services;
    }
}