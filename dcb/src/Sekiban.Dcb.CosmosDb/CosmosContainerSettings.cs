namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Container settings for CosmosDB access.
/// </summary>
public sealed class CosmosContainerSettings
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CosmosContainerSettings"/> class.
    /// </summary>
    /// <param name="name">Container name.</param>
    /// <param name="partitionKeyPath">Partition key path for the container.</param>
    /// <param name="isLegacy">Whether the container uses legacy partitioning.</param>
    public CosmosContainerSettings(string name, string partitionKeyPath, bool isLegacy)
    {
        Name = name;
        PartitionKeyPath = partitionKeyPath;
        IsLegacy = isLegacy;
    }

    /// <summary>
    ///     Container name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Partition key path for the container.
    /// </summary>
    public string PartitionKeyPath { get; }

    /// <summary>
    ///     Whether the container uses legacy partitioning.
    /// </summary>
    public bool IsLegacy { get; }
}
