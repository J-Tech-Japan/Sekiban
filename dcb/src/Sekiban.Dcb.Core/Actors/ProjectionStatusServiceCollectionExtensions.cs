using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Dependency-injection registrations for the passive projection status read boundary.
/// </summary>
public static class ProjectionStatusServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the passive status readers after a provider has registered an
    ///     <see cref="IProjectionStatusStore" /> and an <see cref="IEventStore" />.
    /// </summary>
    public static IServiceCollection AddSekibanDcbProjectionStatusReader(this IServiceCollection services)
    {
        services.AddSekibanDcbSortableUniqueIdGenerator();
        services.TryAddSingleton<ProjectionStatusOptions>();
        services.TryAddSingleton<ProjectionStatusReadWindowCache>();
        services.TryAddTransient<IProjectionStatusReader>(serviceProvider =>
            new ProjectionStatusReader(
                serviceProvider.GetRequiredService<IProjectionStatusStore>(),
                serviceProvider.GetRequiredService<IEventStore>(),
                serviceProvider.GetService<IServiceIdProvider>(),
                serviceProvider.GetRequiredService<ProjectionStatusOptions>(),
                serviceProvider.GetRequiredService<ProjectionStatusReadWindowCache>()));
        services.TryAddTransient<ISerializedProjectionStatusReader>(serviceProvider =>
            new SerializedProjectionStatusReader(
                serviceProvider.GetRequiredService<IProjectionStatusReader>(),
                serviceProvider.GetService<IServiceIdProvider>()));
        return services;
    }
}
