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
    private readonly ProjectionStatusOptions _projectionStatusOptions;

    public PostgresMultiProjectionStateStoreFactory(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
        : this(contextFactory, blobAccessor, new ProjectionStatusOptions())
    {
    }

    /// <summary>Additive options-aware constructor for pre-provisioned status deployments.</summary>
    public PostgresMultiProjectionStateStoreFactory(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IBlobStorageSnapshotAccessor? blobAccessor,
        ProjectionStatusOptions projectionStatusOptions)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _blobAccessor = blobAccessor;
        _projectionStatusOptions = projectionStatusOptions ?? throw new ArgumentNullException(nameof(projectionStatusOptions));
        _projectionStatusOptions.Validate();
    }

    public IMultiProjectionStateStore CreateForService(string serviceId)
    {
        var provider = new FixedServiceIdProvider(serviceId);
        return new PostgresMultiProjectionStateStore(
            _contextFactory,
            provider,
            _blobAccessor,
            _projectionStatusOptions);
    }
}
