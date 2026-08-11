using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Common;

namespace Sekiban.Dcb.Actors;

public static class SortableUniqueIdServiceCollectionExtensions
{
    /// <summary>Registers the process-wide monotonic allocator and service-head seed coordinator.</summary>
    public static IServiceCollection AddSekibanDcbSortableUniqueIdGenerator(this IServiceCollection services)
    {
        services.TryAddSingleton<ISortableUniqueIdGenerator>(serviceProvider =>
            new MonotonicSortableUniqueIdGenerator(
                TimeProvider.System,
                serviceProvider.GetService<ILogger<MonotonicSortableUniqueIdGenerator>>() ??
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MonotonicSortableUniqueIdGenerator>.Instance));
        services.TryAddSingleton<SortableUniqueIdSeedCoordinator>();
        return services;
    }
}
