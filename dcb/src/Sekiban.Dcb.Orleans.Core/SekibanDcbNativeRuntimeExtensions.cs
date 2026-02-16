using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;

namespace Sekiban.Dcb.Orleans;

public static class SekibanDcbNativeRuntimeExtensions
{
    /// <summary>
    ///     Registers the native runtime abstraction services and individual domain type
    ///     interfaces extracted from DcbDomainTypes.
    ///     Call this after DcbDomainTypes has been registered in the container.
    /// </summary>
    public static IServiceCollection AddSekibanDcbNativeRuntime(this IServiceCollection services)
    {
        services.AddSingleton<IProjectionActorHostFactory, NativeProjectionActorHostFactory>();
        services.AddSingleton<ITagStateProjectionPrimitive, NativeTagStateProjectionPrimitive>();

        services.TryAddSingleton<ITagProjectorTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagProjectorTypes);
        services.TryAddSingleton<ITagTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagTypes);
        services.TryAddSingleton<IEventTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().EventTypes);
        services.TryAddSingleton<ITagStatePayloadTypes>(sp => sp.GetRequiredService<DcbDomainTypes>().TagStatePayloadTypes);

        return services;
    }
}
