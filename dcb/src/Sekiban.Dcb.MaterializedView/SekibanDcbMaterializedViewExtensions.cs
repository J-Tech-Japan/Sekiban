using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.MaterializedView;

public static class SekibanDcbMaterializedViewExtensions
{
    public static IServiceCollection AddSekibanDcbMaterializedView(
        this IServiceCollection services,
        Action<MvOptions>? configure = null)
    {
        services.AddOptions<MvOptions>();
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.TryAddSingleton<IMvApplyHostFactory>(sp =>
        {
            var storageInfoProvider = sp.GetService<IMvStorageInfoProvider>() ??
                throw new InvalidOperationException(
                    "IMvStorageInfoProvider is not registered. Call a concrete materialized view provider extension such as AddSekibanDcbMaterializedViewPostgres, AddSekibanDcbMaterializedViewSqlServer, AddSekibanDcbMaterializedViewMySql, or AddSekibanDcbMaterializedViewSqlite.");
            return new NativeMvApplyHostFactory(
                sp.GetServices<IMaterializedViewProjector>(),
                sp.GetRequiredService<IEventTypes>(),
                storageInfoProvider);
        });
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Selects a provider executor constructor during DI registration. This is only a constructor
    /// registration helper; it never calls <c>CreateForService</c> and never resolves or caches an
    /// event store for an operation.
    /// </summary>
    public static TExecutor CreateMaterializedViewExecutor<TExecutor>(
        IServiceProvider services,
        string connectionString,
        Func<
            IEventStoreFactory,
            IMvRegistryStore,
            IOptions<MvOptions>,
            ILogger<TExecutor>,
            string,
            TExecutor> factoryConstructor,
        Func<
            IEventStore,
            IServiceIdProvider,
            IMvRegistryStore,
            IOptions<MvOptions>,
            ILogger<TExecutor>,
            string,
            TExecutor> legacyConstructor)
    {
        var options = services.GetRequiredService<IOptions<MvOptions>>();
        var registry = services.GetRequiredService<IMvRegistryStore>();
        var logger = services.GetRequiredService<ILogger<TExecutor>>();
        var eventStoreFactory = services.GetService<IEventStoreFactory>();
        return eventStoreFactory is not null
            ? factoryConstructor(eventStoreFactory, registry, options, logger, connectionString)
            : legacyConstructor(
                services.GetRequiredService<IEventStore>(),
                services.GetRequiredService<IServiceIdProvider>(),
                registry,
                options,
                logger,
                connectionString);
    }

    public static IServiceCollection AddMaterializedView<TView>(this IServiceCollection services)
        where TView : class, IMaterializedViewProjector
    {
        services.TryAddSingleton<TView>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMaterializedViewProjector, TView>(sp => sp.GetRequiredService<TView>()));
        return services;
    }

    /// <summary>
    /// Registers one hosted catch-up worker bound to an exact service id. Call once per service in a multi-service host.
    /// The provider extension must be called with <c>registerHostedWorker: false</c> when using this method.
    /// </summary>
    public static IServiceCollection AddSekibanDcbMaterializedViewWorkerForService(
        this IServiceCollection services,
        string serviceId)
    {
        var normalized = ServiceIdValidator.NormalizeAndValidate(serviceId);
        if (string.Equals(normalized, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The service-bound worker API requires a non-default ServiceId. Use MvOptions.AllowDefaultServiceId for explicit single-service compatibility.",
                nameof(serviceId));
        }

        services.AddSingleton<IHostedService>(sp =>
            new MvCatchUpWorker(
                sp.GetRequiredService<IMvApplyHostFactory>(),
                sp.GetRequiredService<IMvExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MvOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MvCatchUpWorker>>(),
                normalized));
        return services;
    }
}
