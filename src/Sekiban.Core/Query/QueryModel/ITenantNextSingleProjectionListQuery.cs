using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}