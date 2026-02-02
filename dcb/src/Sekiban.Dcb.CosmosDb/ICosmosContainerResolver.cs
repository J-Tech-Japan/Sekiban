namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Resolves Cosmos container settings for a given ServiceId.
/// </summary>
public interface ICosmosContainerResolver
{
    /// <summary>
    ///     Resolves the events container settings for the specified ServiceId.
    /// </summary>
    CosmosContainerSettings ResolveEventsContainer(string serviceId);

    /// <summary>
    ///     Resolves the tags container settings for the specified ServiceId.
    /// </summary>
    CosmosContainerSettings ResolveTagsContainer(string serviceId);

    /// <summary>
    ///     Resolves the multi-projection states container settings for the specified ServiceId.
    /// </summary>
    CosmosContainerSettings ResolveStatesContainer(string serviceId);
}
