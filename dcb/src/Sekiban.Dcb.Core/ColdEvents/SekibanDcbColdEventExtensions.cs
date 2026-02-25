using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.ColdEvents;

public static class SekibanDcbColdEventExtensions
{
    public static IServiceCollection AddSekibanDcbColdEventDefaults(this IServiceCollection services)
    {
        var notSupported = new NotSupportedColdEventStore();
        services.TryAddSingleton<IColdEventStoreFeature>(notSupported);
        services.TryAddSingleton<IColdEventProgressReader>(notSupported);
        services.TryAddSingleton<IColdEventExporter>(notSupported);
        return services;
    }

    public static IServiceCollection AddSekibanDcbColdEvents(
        this IServiceCollection services,
        Action<ColdEventStoreOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddSingleton<ColdExporter>();
        services.AddSingleton<IColdEventExporter>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddSingleton<IColdEventProgressReader>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddSingleton<IColdEventStoreFeature>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddHostedService<ColdExportBackgroundService>();
        return services;
    }

    public static IServiceCollection AddSekibanDcbColdEventHybridRead(
        this IServiceCollection services)
    {
        var existingDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventStore));
        if (existingDescriptor is null)
        {
            throw new InvalidOperationException(
                "IEventStore must be registered before adding hybrid read support");
        }

        services.Replace(ServiceDescriptor.Singleton<IEventStore>(sp =>
        {
            var hotStore = ResolveFromDescriptor(sp, existingDescriptor);
            return new HybridEventStore(
                hotStore,
                sp.GetRequiredService<IColdObjectStorage>(),
                sp.GetRequiredService<IServiceIdProvider>(),
                sp.GetRequiredService<IOptions<ColdEventStoreOptions>>(),
                sp.GetRequiredService<ILogger<HybridEventStore>>());
        }));

        return services;
    }

    private static IEventStore ResolveFromDescriptor(IServiceProvider sp, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IEventStore instance)
        {
            return instance;
        }
        if (descriptor.ImplementationFactory is not null)
        {
            return (IEventStore)descriptor.ImplementationFactory(sp);
        }
        if (descriptor.ImplementationType is not null)
        {
            return (IEventStore)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }
        throw new InvalidOperationException("Cannot resolve inner IEventStore from existing registration");
    }
}
