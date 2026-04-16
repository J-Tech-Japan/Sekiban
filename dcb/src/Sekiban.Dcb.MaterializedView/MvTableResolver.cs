using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView;

public sealed class MvTableResolver : IMvTableResolver
{
    private readonly IMvRegistryStore _registryStore;
    private readonly IServiceIdProvider _serviceIdProvider;

    public MvTableResolver(IMvRegistryStore registryStore, IServiceIdProvider serviceIdProvider)
    {
        _registryStore = registryStore;
        _serviceIdProvider = serviceIdProvider;
    }

    public async Task<MvRegistryEntry> ResolveAsync(
        IMaterializedViewProjector projector,
        string logicalTable,
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

        var entry = entries.FirstOrDefault(candidate =>
            string.Equals(candidate.LogicalTable, logicalTable, StringComparison.Ordinal));

        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Materialized view table '{logicalTable}' is not registered for {projector.ViewName} v{projector.ViewVersion}.");
        }

        return entry;
    }
}
