using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : ITenantNextMultiProjectionListQueryWithPagingAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
