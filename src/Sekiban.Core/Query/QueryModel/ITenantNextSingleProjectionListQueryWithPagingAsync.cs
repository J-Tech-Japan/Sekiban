using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TQuery, TOutput> :
    INextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
    where TQuery : ITenantNextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
