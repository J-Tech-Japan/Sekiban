using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Factory for creating ServiceId-scoped CosmosDbEventStore instances.
/// </summary>
public sealed class CosmosDbEventStoreFactory : IEventStoreFactory
{
    private readonly CosmosDbContext _context;
    private readonly DcbDomainTypes _domainTypes;
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    ///     Creates a CosmosDb event store factory.
    /// </summary>
    public CosmosDbEventStoreFactory(
        CosmosDbContext context,
        DcbDomainTypes domainTypes,
        ICosmosContainerResolver containerResolver,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IEventStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        var logger = _loggerFactory?.CreateLogger<CosmosDbEventStore>();
        return new CosmosDbEventStore(_context, _domainTypes, provider, _containerResolver, logger);
    }
}
