using Microsoft.Extensions.Logging;
namespace Sekiban.Dcb.CosmosDb.Repair;

/// <summary>
///     Builds a <see cref="CosmosDbTagRepairService" /> bound to one lineage.
///     The service id is passed explicitly rather than pulled from an ambient
///     <c>IServiceIdProvider</c>: an operator repairing tenant A's index should not be able to touch tenant
///     B's because of what happened to be in scope. Resolving the containers here, once, per service id
///     makes a cross-lineage repair structurally impossible rather than merely discouraged.
/// </summary>
public sealed class CosmosDbTagRepairServiceFactory
{
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly CosmosDbContext _context;
    private readonly ILogger<CosmosDbTagRepairService>? _logger;

    /// <summary>
    ///     Creates a factory over a Cosmos context and container resolver.
    /// </summary>
    public CosmosDbTagRepairServiceFactory(
        CosmosDbContext context,
        ICosmosContainerResolver containerResolver,
        ILogger<CosmosDbTagRepairService>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _logger = logger;
    }

    /// <summary>
    ///     Creates a repair service for exactly one (serviceId, events container, tags container) lineage.
    /// </summary>
    public async Task<CosmosDbTagRepairService> CreateAsync(string serviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var eventsSettings = _containerResolver.ResolveEventsContainer(serviceId);
        var tagsSettings = _containerResolver.ResolveTagsContainer(serviceId);
        var eventsContainer = await _context.GetEventsContainerAsync(eventsSettings).ConfigureAwait(false);
        var tagsContainer = await _context.GetTagsContainerAsync(tagsSettings).ConfigureAwait(false);

        return new CosmosDbTagRepairService(
            serviceId,
            new CosmosContainerRepairEventSource(eventsContainer, serviceId),
            new CosmosContainerRepairStore(tagsContainer),
            _logger);
    }
}
