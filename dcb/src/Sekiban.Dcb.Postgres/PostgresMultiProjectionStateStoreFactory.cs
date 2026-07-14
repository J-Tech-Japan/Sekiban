using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Capabilities;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Factory for creating ServiceId-scoped PostgresMultiProjectionStateStore instances.
/// </summary>
public sealed class PostgresMultiProjectionStateStoreFactory : IMultiProjectionStateStoreFactory, IStorageDurabilityDescriptorProvider
{
    /// <summary>Every store this factory builds is a Postgres one.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "Postgres (per-service factory)");

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
