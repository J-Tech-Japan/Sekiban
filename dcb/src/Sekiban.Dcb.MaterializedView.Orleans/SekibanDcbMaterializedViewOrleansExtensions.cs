using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans.Streams;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public static class SekibanDcbMaterializedViewOrleansExtensions
{
    public static IServiceCollection AddSekibanDcbMaterializedViewOrleans(
        this IServiceCollection services,
        bool activateOnStartup = true)
    {
        services.TryAddSingleton<IEventSubscriptionResolver, DefaultOrleansEventSubscriptionResolver>();
        services.TryAddSingleton<IMvOrleansQueryAccessor, MvOrleansQueryAccessor>();
        if (activateOnStartup)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MaterializedViewGrainActivator>());
        }

        return services;
    }
}
