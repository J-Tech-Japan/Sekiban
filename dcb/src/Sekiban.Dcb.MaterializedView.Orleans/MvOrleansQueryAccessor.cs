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
    }

    public string ServiceId { get; }
    public MvDbType DatabaseType { get; }
    public string ConnectionString { get; }
    public IMaterializedViewGrain Grain { get; }
    public IReadOnlyList<MvRegistryEntry> Entries { get; }

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

        var grainKey = MvGrainKey.Build(serviceId, projector.ViewName, projector.ViewVersion);
        var grain = _client.GetGrain<IMaterializedViewGrain>(grainKey);
        await grain.EnsureStartedAsync().ConfigureAwait(false);

        var entries = await _registryStore.GetEntriesAsync(
            serviceId,
            projector.ViewName,
            projector.ViewVersion,
            cancellationToken).ConfigureAwait(false);

        var storageInfo = _storageInfoProvider.GetStorageInfo();
        return new MvOrleansQueryContext(serviceId, storageInfo.DatabaseType, storageInfo.ConnectionString, grain, entries);
    }
}
