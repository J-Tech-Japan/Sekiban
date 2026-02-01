using Microsoft.Extensions.Logging;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Factory for creating ServiceId-scoped CosmosMultiProjectionStateStore instances.
/// </summary>
public sealed class CosmosDbMultiProjectionStateStoreFactory : IMultiProjectionStateStoreFactory
{
    private readonly CosmosDbContext _context;
    private readonly ICosmosContainerResolver _containerResolver;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    ///     Creates a CosmosDB multi-projection state store factory.
    /// </summary>
    public CosmosDbMultiProjectionStateStoreFactory(
        CosmosDbContext context,
        ICosmosContainerResolver containerResolver,
        IBlobStorageSnapshotAccessor? blobAccessor = null,
        ILoggerFactory? loggerFactory = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _blobAccessor = blobAccessor;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public IMultiProjectionStateStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        _ = _loggerFactory; // reserved for future logging needs
        return new CosmosMultiProjectionStateStore(_context, provider, _containerResolver, _blobAccessor);
    }
}
