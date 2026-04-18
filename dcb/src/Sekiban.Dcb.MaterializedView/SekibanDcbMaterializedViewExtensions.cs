using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Domains;
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

    public static IServiceCollection AddMaterializedView<TView>(this IServiceCollection services)
        where TView : class, IMaterializedViewProjector
    {
        services.TryAddSingleton<TView>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMaterializedViewProjector, TView>(sp => sp.GetRequiredService<TView>()));
        return services;
    }
}
