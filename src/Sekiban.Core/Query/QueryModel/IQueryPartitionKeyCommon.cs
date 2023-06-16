using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryPartitionKeyCommon
{
    string GetRootPartitionKey() => IMultiProjectionService.ProjectionAllRootPartitions;
}
