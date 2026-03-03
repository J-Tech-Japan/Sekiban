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
    public CosmosContainerSettings(string name, string partitionKeyPath)
    {
        Name = name;
        PartitionKeyPath = partitionKeyPath;
    }

    /// <summary>
    ///     Container name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Partition key path for the container.
    /// </summary>
    public string PartitionKeyPath { get; }

}
