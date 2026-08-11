using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public sealed class MvOrleansQueryContext
{
    public MvOrleansQueryContext(
        string serviceId,
        MvDbType databaseType,
        string connectionString,
        IMaterializedViewGrain grain,
        IReadOnlyList<MvRegistryEntry> entries)
    {
        ServiceId = serviceId;
        DatabaseType = databaseType;
        ConnectionString = connectionString;
        Grain = grain;
        Entries = entries;
        ViewVersion = entries.FirstOrDefault()?.ViewVersion;
    }

    public string ServiceId { get; }
    public MvDbType DatabaseType { get; }
    public string ConnectionString { get; }
    public IMaterializedViewGrain Grain { get; }
    public IReadOnlyList<MvRegistryEntry> Entries { get; }
    public int? ViewVersion { get; }
    public MvActiveEntry? ActivePointer { get; init; }

    public MvRegistryEntry GetRequiredTable(string logicalTable)
    {
        var entry = Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.LogicalTable, logicalTable, StringComparison.Ordinal));
        if (entry is null)
        {
            throw new InvalidOperationException($"Materialized view table '{logicalTable}' is not registered.");
        }

        return entry;
    }
}

public interface IMvOrleansQueryAccessor
{
    Task<MvOrleansQueryContext> GetAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Explicit version-pinned diagnostics. Ordinary reads must use <see cref="GetAsync"/>.</summary>
    Task<MvOrleansQueryContext> GetPinnedAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This accessor does not support explicit version-pinned diagnostics.");
}

public sealed class MvOrleansQueryAccessor : IMvOrleansQueryAccessor
{
    private readonly IClusterClient _client;
    private readonly IMvRegistryStore _registryStore;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly IMvStorageInfoProvider _storageInfoProvider;

    public MvOrleansQueryAccessor(
        IClusterClient client,
        IMvRegistryStore registryStore,
        IServiceIdProvider serviceIdProvider,
        IMvStorageInfoProvider storageInfoProvider)
    {
        _client = client;
        _registryStore = registryStore;
        _serviceIdProvider = serviceIdProvider;
        _storageInfoProvider = storageInfoProvider;
    }

    public async Task<MvOrleansQueryContext> GetAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        serviceId = string.IsNullOrWhiteSpace(serviceId)
            ? _serviceIdProvider.GetCurrentServiceId()
            : serviceId;

        var active = await _registryStore.GetActiveAsync(serviceId, projector.ViewName, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException($"Materialized view '{projector.ViewName}' has no active generation.");
        var entries = await _registryStore.GetEntriesAsync(
            serviceId,
            projector.ViewName,
            active.ActiveVersion,
            cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Active materialized view '{projector.ViewName}' version {active.ActiveVersion} does not exist.");
        }

        var grainKey = MvGrainKey.Build(serviceId, projector.ViewName, active.ActiveVersion);
        var grain = _client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync().ConfigureAwait(false);

        var storageInfo = _storageInfoProvider.GetStorageInfo();
        return new MvOrleansQueryContext(serviceId, storageInfo.DatabaseType, storageInfo.ConnectionString, grain, entries)
        {
            ActivePointer = active
        };
    }

    public async Task<MvOrleansQueryContext> GetPinnedAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        serviceId = string.IsNullOrWhiteSpace(serviceId)
            ? _serviceIdProvider.GetCurrentServiceId()
            : serviceId;
        var entries = await _registryStore.GetEntriesAsync(
            serviceId,
            projector.ViewName,
            projector.ViewVersion,
            cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException(
                $"Pinned materialized view '{projector.ViewName}' version {projector.ViewVersion} does not exist.");
        }

        var grain = _client.GetGrain<IMaterializedViewGrain>(
            MvGrainKey.Build(serviceId, projector.ViewName, projector.ViewVersion));
        await grain.EnsureStartedAsync().ConfigureAwait(false);
        var storageInfo = _storageInfoProvider.GetStorageInfo();
        return new MvOrleansQueryContext(serviceId, storageInfo.DatabaseType, storageInfo.ConnectionString, grain, entries);
    }
}
