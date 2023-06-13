using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryInputCommon
{
    string RootPartitionKey => IMultiProjectionService.ProjectionAllPartitions;
}
