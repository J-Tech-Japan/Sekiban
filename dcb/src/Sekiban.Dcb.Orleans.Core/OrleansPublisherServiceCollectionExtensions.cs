using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Orleans;

/// <summary>Convenience registration for the bounded Orleans publisher and its diagnostics singleton.</summary>
public static class OrleansPublisherServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanDcbOrleansEventPublisher(
        this IServiceCollection services,
        Action<OrleansEventPublisherOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new OrleansEventPublisherOptions();
        configure?.Invoke(options);
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<OrleansEventPublisher>(sp =>
            new OrleansEventPublisher(
                sp.GetRequiredService<global::Orleans.IClusterClient>(),
                sp.GetRequiredService<Sekiban.Dcb.Actors.IStreamDestinationResolver>(),
                sp.GetRequiredService<Sekiban.Dcb.DcbDomainTypes>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OrleansEventPublisher>>(),
                sp.GetRequiredService<OrleansEventPublisherOptions>(),
                sp.GetService<Sekiban.Dcb.ServiceId.IServiceIdProvider>()));
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<OrleansEventPublisher>());
        services.TryAddSingleton<IOrleansPublisherDiagnostics>(sp =>
            sp.GetRequiredService<OrleansEventPublisher>());
        return services;
    }
}
