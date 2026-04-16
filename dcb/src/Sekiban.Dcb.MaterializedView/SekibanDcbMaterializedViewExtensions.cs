using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView;

public static class SekibanDcbMaterializedViewExtensions
{
    public static IServiceCollection AddSekibanDcbMaterializedView(
        this IServiceCollection services,
        Action<MvOptions>? configure = null)
    {
        services.AddOptions<MvOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IMvTableResolver, MvTableResolver>();

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
