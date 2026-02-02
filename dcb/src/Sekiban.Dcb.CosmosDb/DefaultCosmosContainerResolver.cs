using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Default resolver that routes "default" tenant to legacy containers when enabled.
/// </summary>
public sealed class DefaultCosmosContainerResolver : ICosmosContainerResolver
{
    private readonly CosmosDbEventStoreOptions _options;

    /// <summary>
    ///     Creates a resolver for container routing based on ServiceId.
    /// </summary>
    public DefaultCosmosContainerResolver(CosmosDbEventStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public CosmosContainerSettings ResolveEventsContainer(string serviceId) =>
        ResolveContainer(
            serviceId,
            _options.EventsContainerName,
            _options.LegacyEventsContainerName,
            legacyPartitionKeyPath: "/id");

    /// <inheritdoc />
    public CosmosContainerSettings ResolveTagsContainer(string serviceId) =>
        ResolveContainer(
            serviceId,
            _options.TagsContainerName,
            _options.LegacyTagsContainerName,
            legacyPartitionKeyPath: "/tag");

    /// <inheritdoc />
    public CosmosContainerSettings ResolveStatesContainer(string serviceId) =>
        ResolveContainer(
            serviceId,
            _options.MultiProjectionStatesContainerName,
            _options.LegacyMultiProjectionStatesContainerName,
            legacyPartitionKeyPath: "/partitionKey");

    private CosmosContainerSettings ResolveContainer(
        string serviceId,
        string v2Name,
        string legacyName,
        string legacyPartitionKeyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var isDefault = string.Equals(serviceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal);
        var useLegacy = _options.UseLegacyPartitionKeyPaths && isDefault;

        if (useLegacy)
        {
            return new CosmosContainerSettings(legacyName, legacyPartitionKeyPath, isLegacy: true);
        }

        return new CosmosContainerSettings(v2Name, "/pk", isLegacy: false);
    }
}
