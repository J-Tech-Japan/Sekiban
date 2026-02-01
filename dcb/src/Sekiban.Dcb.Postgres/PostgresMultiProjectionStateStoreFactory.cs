using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Factory for creating ServiceId-scoped PostgresMultiProjectionStateStore instances.
/// </summary>
public sealed class PostgresMultiProjectionStateStoreFactory : IMultiProjectionStateStoreFactory
{
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;

    public PostgresMultiProjectionStateStoreFactory(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _blobAccessor = blobAccessor;
    }

    public IMultiProjectionStateStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        return new PostgresMultiProjectionStateStore(_contextFactory, provider, _blobAccessor);
    }
}
