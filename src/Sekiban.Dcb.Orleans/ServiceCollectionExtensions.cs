using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Extension methods for configuring Sekiban DCB with Orleans
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Sekiban DCB Orleans services to the service collection
    /// </summary>
    public static IServiceCollection AddSekibanDcbOrleans(
        this IServiceCollection services,
        DcbDomainTypes domainTypes,
        Action<ISiloBuilder>? configureSilo = null)
    {
        // Register domain types
        services.AddSingleton(domainTypes);
        
        // Register command executor
        services.AddScoped<ISekibanExecutor, OrleansCommandExecutor>();
        services.AddScoped<ICommandExecutor>(sp => sp.GetRequiredService<ISekibanExecutor>());
        services.AddScoped<IActorObjectAccessor, OrleansActorObjectAccessor>();
        
        // Configure Orleans silo
        services.AddOrleans(builder =>
        {
            builder.UseLocalhostClustering();
            
            // Configure grain storage
            builder.AddMemoryGrainStorageAsDefault();
            
            // Add streaming provider for events
            builder.AddMemoryStreams("EventStreamProvider");
            
            // Apply custom configuration if provided
            configureSilo?.Invoke(builder);
        });
        
        return services;
    }
    
    /// <summary>
    /// Add Sekiban DCB Orleans client services
    /// </summary>
    public static IServiceCollection AddSekibanDcbOrleansClient(
        this IServiceCollection services,
        DcbDomainTypes domainTypes,
        Action<IClientBuilder>? configureClient = null)
    {
        // Register domain types
        services.AddSingleton(domainTypes);
        
        // Register command executor and accessor as singletons for client
        services.AddSingleton<ISekibanExecutor, OrleansCommandExecutor>();
        services.AddSingleton<ICommandExecutor>(sp => sp.GetRequiredService<ISekibanExecutor>());
        services.AddSingleton<IActorObjectAccessor, OrleansActorObjectAccessor>();
        
        // Configure Orleans client
        services.AddOrleansClient(builder =>
        {
            builder.UseLocalhostClustering();
            
            // Add streaming provider for events
            builder.AddMemoryStreams("EventStreamProvider");
            
            // Apply custom configuration if provided
            configureClient?.Invoke(builder);
        });
        
        return services;
    }
    
    /// <summary>
    /// Configure Orleans silo for Sekiban DCB
    /// </summary>
    public static ISiloBuilder ConfigureSekibanDcb(
        this ISiloBuilder builder,
        DcbDomainTypes domainTypes)
    {
        // Configure clustering
        builder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "sekiban-dcb-cluster";
            options.ServiceId = "sekiban-dcb-service";
        });
        
        // Configure grain collection
        builder.Configure<GrainCollectionOptions>(options =>
        {
            // Set appropriate timeouts for grain deactivation
            options.CollectionAge = TimeSpan.FromMinutes(10);
            options.DeactivationTimeout = TimeSpan.FromMinutes(2);
        });
        
        // Note: Application parts are automatically discovered in Orleans 7+
        // If using an older version, you may need to manually configure application parts
        
        return builder;
    }
    
    /// <summary>
    /// Configure Orleans client for Sekiban DCB
    /// </summary>
    public static IClientBuilder ConfigureSekibanDcb(
        this IClientBuilder builder,
        DcbDomainTypes domainTypes)
    {
        // Configure clustering
        builder.Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "sekiban-dcb-cluster";
            options.ServiceId = "sekiban-dcb-service";
        });
        
        // Note: Application parts are automatically discovered in Orleans 7+
        // If using an older version, you may need to manually configure application parts
        
        return builder;
    }
}