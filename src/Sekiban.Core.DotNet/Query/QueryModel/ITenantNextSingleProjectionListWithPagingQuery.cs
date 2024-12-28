using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : ITenantNextSingleProjectionListWithPagingQuery<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
