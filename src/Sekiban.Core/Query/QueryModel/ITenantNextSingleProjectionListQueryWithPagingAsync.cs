using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionListQueryWithPagingAsync<TSingleProjectionPayloadCommon, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}