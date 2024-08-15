using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TOutput> :
    INextMultiProjectionListQuery<TMultiProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
