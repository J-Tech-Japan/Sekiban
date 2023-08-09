using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Parameter Interface for Partition Key.
///     Query developers does not need to implement this interface directly.
/// </summary>
public interface IQueryPartitionKeyCommon
{
    /// <summary>
    ///     returns Partition Key for Query.
    /// </summary>
    /// <returns></returns>
    string GetRootPartitionKey() => IMultiProjectionService.ProjectionAllRootPartitions;
}
