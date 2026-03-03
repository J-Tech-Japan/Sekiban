namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Default resolver for Cosmos container settings.
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
        ResolveContainer(serviceId, _options.EventsContainerName);

    /// <inheritdoc />
    public CosmosContainerSettings ResolveTagsContainer(string serviceId) =>
        ResolveContainer(serviceId, _options.TagsContainerName);

    /// <inheritdoc />
    public CosmosContainerSettings ResolveStatesContainer(string serviceId) =>
        ResolveContainer(serviceId, _options.MultiProjectionStatesContainerName);

    private static CosmosContainerSettings ResolveContainer(
        string serviceId,
        string containerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        return new CosmosContainerSettings(containerName, "/pk");
    }
}
