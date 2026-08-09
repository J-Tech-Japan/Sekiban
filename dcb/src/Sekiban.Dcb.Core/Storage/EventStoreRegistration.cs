using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Shared DI registration mechanics for provider event-store aliases. This only wires the already-created
///     provider store into its interface aliases; it does not resolve or cache a service-scoped event store.
/// </summary>
public static class EventStoreRegistration
{
    public static IServiceCollection AddEventStoreAliases<TEventStore>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TEventStore : class, IHotEventStore
    {
        services.Add(ServiceDescriptor.Describe(typeof(TEventStore), typeof(TEventStore), lifetime));
        services.Add(ServiceDescriptor.Describe(
            typeof(IHotEventStore),
            provider => provider.GetRequiredService<TEventStore>(),
            lifetime));
        services.Add(ServiceDescriptor.Describe(
            typeof(IEventStore),
            provider => provider.GetRequiredService<IHotEventStore>(),
            lifetime));
        return services;
    }
}
