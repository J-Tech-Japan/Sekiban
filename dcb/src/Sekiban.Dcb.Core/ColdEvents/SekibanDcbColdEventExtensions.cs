using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        services.TryAddSingleton<IOptions<ColdEventStoreOptions>>(
            Options.Create(new ColdEventStoreOptions { Enabled = false }));
        services.TryAddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IColdEventStoreFeature>(notSupported);
        services.TryAddSingleton<IColdEventProgressReader>(notSupported);
        services.TryAddSingleton<IColdEventExporter>(notSupported);
        services.TryAddSingleton<IColdEventCatalogReader>(notSupported);
        services.TryAddSingleton<IColdSegmentFormatHandler, JsonlColdSegmentFormatHandler>();
        services.TryAddSingleton<ColdExportCycleRunner>();
        return services;
    }

    public static IServiceCollection AddSekibanDcbColdEvents(
        this IServiceCollection services,
        Action<ColdEventStoreOptions> configureOptions,
        bool addBackgroundService = true)
    {
        services.Configure(configureOptions);
        RegisterColdEventCoreServices(services, addBackgroundService);
        return services;
    }

    public static IServiceCollection AddSekibanDcbColdEvents(
        this IServiceCollection services,
        ColdEventStoreOptions options,
        bool addBackgroundService = true)
    {
        services.AddSingleton<IOptions<ColdEventStoreOptions>>(Options.Create(options));
        RegisterColdEventCoreServices(services, addBackgroundService);
        return services;
    }

    private static void RegisterColdEventCoreServices(IServiceCollection services, bool addBackgroundService)
    {
        services.TryAddSingleton<ColdExportCycleRunner>();
        services.AddSingleton<ColdExporter>();
        services.AddSingleton<IColdEventExporter>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddSingleton<IColdEventProgressReader>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddSingleton<IColdEventStoreFeature>(sp => sp.GetRequiredService<ColdExporter>());
        services.AddSingleton<ColdCatalogReader>();
        services.AddSingleton<IColdEventCatalogReader>(sp => sp.GetRequiredService<ColdCatalogReader>());
        services.TryAddSingleton<IColdSegmentFormatHandler, JsonlColdSegmentFormatHandler>();
        if (addBackgroundService)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, ColdExportBackgroundService>());
        }
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
            var hotStore = ResolveFromDescriptor<IEventStore>(sp, existingDescriptor);
            return new HybridEventStore(
                hotStore,
                sp.GetRequiredService<IColdObjectStorage>(),
                sp.GetRequiredService<IColdSegmentFormatHandler>(),
                sp.GetRequiredService<IServiceIdProvider>(),
                sp.GetRequiredService<IOptions<ColdEventStoreOptions>>(),
                sp.GetRequiredService<ILogger<HybridEventStore>>(),
                new RetainedColdSegmentHolder(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System);
        }));

        var factoryDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEventStoreFactory));
        if (factoryDescriptor is not null)
        {
            // Replace the last registration in place: that is the one DI resolves.
            var index = services.IndexOf(factoryDescriptor);
            services[index] = ServiceDescriptor.Singleton<IEventStoreFactory>(sp => new HybridEventStoreFactory(
                ResolveFromDescriptor<IEventStoreFactory>(sp, factoryDescriptor),
                sp.GetRequiredService<IColdObjectStorage>(),
                sp.GetRequiredService<IColdSegmentFormatHandler>(),
                sp.GetRequiredService<IOptions<ColdEventStoreOptions>>(),
                sp.GetRequiredService<ILogger<HybridEventStore>>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System));
        }

        return services;
    }

    private static T ResolveFromDescriptor<T>(IServiceProvider sp, ServiceDescriptor descriptor) where T : class
    {
        if (descriptor.ImplementationInstance is T instance)
        {
            return instance;
        }
        if (descriptor.ImplementationFactory is not null)
        {
            return (T)descriptor.ImplementationFactory(sp);
        }
        if (descriptor.ImplementationType is not null)
        {
            return (T)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }
        throw new InvalidOperationException("Cannot resolve inner store from existing registration");
    }
}
