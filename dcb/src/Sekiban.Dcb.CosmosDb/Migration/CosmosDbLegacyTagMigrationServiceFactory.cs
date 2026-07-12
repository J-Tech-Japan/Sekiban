using Microsoft.Extensions.Logging;
using Sekiban.Dcb.CosmosDb.Repair;
namespace Sekiban.Dcb.CosmosDb.Migration;

/// <summary>
///     Builds a <see cref="CosmosDbLegacyTagMigrationService" /> bound to one lineage.
///     The service id is explicit, never ambient: an operator cleaning up tenant A's index must not be able
///     to delete rows from tenant B's because of what happened to be in scope. The containers are resolved
///     once, here, so a cross-lineage migration is impossible rather than merely discouraged.
/// </summary>
public sealed class CosmosDbLegacyTagMigrationServiceFactory
{
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly CosmosDbContext _context;
    private readonly ILogger<CosmosDbLegacyTagMigrationService>? _logger;

    /// <summary>Creates a factory over a Cosmos context and container resolver.</summary>
    public CosmosDbLegacyTagMigrationServiceFactory(
        CosmosDbContext context,
        ICosmosContainerResolver containerResolver,
        ILogger<CosmosDbLegacyTagMigrationService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _logger = logger;
    }

    /// <summary>Creates a migration service for exactly one (serviceId, events, tags) lineage.</summary>
    public async Task<CosmosDbLegacyTagMigrationService> CreateAsync(string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var eventsSettings = _containerResolver.ResolveEventsContainer(serviceId);
        var tagsSettings = _containerResolver.ResolveTagsContainer(serviceId);
        var eventsContainer = await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
        var tagsContainer = await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);

        return new CosmosDbLegacyTagMigrationService(
            serviceId,
            eventsSettings.Name,
            tagsSettings.Name,
            new CosmosContainerRepairEventSource(eventsContainer, serviceId),
            new CosmosContainerMigrationStore(tagsContainer),
            _logger);
    }
}
