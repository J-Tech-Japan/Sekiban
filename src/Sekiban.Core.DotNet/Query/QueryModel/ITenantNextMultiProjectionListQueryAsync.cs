using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : ITenantNextMultiProjectionListQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    IEquatable<TQuery>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
