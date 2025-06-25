using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Extension methods for configuring Dapr serialization
/// </summary>
public static class DaprSerializationExtensions
{
    /// <summary>
    /// Adds Sekiban Dapr serialization services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configure">Optional configuration action</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddSekibanDaprSerialization(
        this IServiceCollection services,
        Action<DaprSerializationOptions>? configure = null)
    {
        // Configure options
        services.Configure<DaprSerializationOptions>(options =>
        {
            configure?.Invoke(options);
        });

        // Register type registry as singleton
        services.AddSingleton<IDaprTypeRegistry, DaprTypeRegistry>();

        // Register serialization service
        services.AddSingleton<IDaprSerializationService, DaprSerializationService>();

        // Configure JSON options for ASP.NET Core
        services.ConfigureHttpJsonOptions(options =>
        {
            var serializationOptions = options.SerializerOptions;
            var daprOptions = services.BuildServiceProvider().GetRequiredService<IOptions<DaprSerializationOptions>>().Value;
            
            // Copy settings from Dapr options
            serializationOptions.DefaultIgnoreCondition = daprOptions.JsonSerializerOptions.DefaultIgnoreCondition;
            serializationOptions.PropertyNamingPolicy = daprOptions.JsonSerializerOptions.PropertyNamingPolicy;
            serializationOptions.WriteIndented = daprOptions.JsonSerializerOptions.WriteIndented;
            
            foreach (var converter in daprOptions.JsonSerializerOptions.Converters)
            {
                serializationOptions.Converters.Add(converter);
            }
        });

        return services;
    }

    /// <summary>
    /// Adds a cached layer on top of the serialization service
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection AddCachedDaprSerialization(this IServiceCollection services)
    {
        services.AddMemoryCache();
        // Replace the existing registration with the cached version
        services.AddSingleton<IDaprSerializationService>(sp =>
        {
            var innerService = new DaprSerializationService(
                sp.GetRequiredService<IDaprTypeRegistry>(),
                sp.GetRequiredService<IOptions<DaprSerializationOptions>>(),
                sp.GetRequiredService<ILogger<DaprSerializationService>>());
            
            return new CachedDaprSerializationService(
                innerService,
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<ILogger<CachedDaprSerializationService>>());
        });
        return services;
    }

    /// <summary>
    /// Registers domain types with the type registry
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="registrationAction">Action to register types</param>
    /// <returns>The service collection</returns>
    public static IServiceCollection RegisterDaprDomainTypes(
        this IServiceCollection services,
        Action<IDaprTypeRegistry> registrationAction)
    {
        services.AddSingleton<IDaprTypeRegistry>(sp =>
        {
            var registry = new DaprTypeRegistry();
            registrationAction(registry);
            return registry;
        });

        return services;
    }
}