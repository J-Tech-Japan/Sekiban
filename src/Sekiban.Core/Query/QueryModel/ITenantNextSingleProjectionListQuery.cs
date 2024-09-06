using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : ITenantNextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
