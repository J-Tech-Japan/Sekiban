using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : ITenantNextMultiProjectionListWithPagingQuery<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
