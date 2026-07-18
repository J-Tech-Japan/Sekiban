using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Capabilities;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     Shared DI registration for the SEK-G16 conditional-append capability. A provider that has already registered its
///     event store as <see cref="IHotEventStore" /> and whose store implements <see cref="IConditionalEventStore" /> +
///     <see cref="IWriteConditionCapabilityProvider" /> calls this once so those interfaces resolve to the SAME singleton
///     — avoiding the identical registration boilerplate being repeated in every provider's extension overload.
/// </summary>
public static class ConditionalEventStoreRegistration
{
    public static IServiceCollection AddConditionalEventStoreCapabilities(this IServiceCollection services)
    {
        services.AddSingleton<IConditionalEventStore>(sp => (IConditionalEventStore)sp.GetRequiredService<IHotEventStore>());
        services.AddSingleton<IWriteConditionCapabilityProvider>(
            sp => (IWriteConditionCapabilityProvider)sp.GetRequiredService<IHotEventStore>());
        return services;
    }
}
