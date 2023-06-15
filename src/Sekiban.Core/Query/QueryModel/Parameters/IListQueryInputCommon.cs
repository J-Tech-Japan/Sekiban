using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IListQueryInputCommon
{
    string RootPartitionKey => IMultiProjectionService.ProjectionAllPartitions;
}
