using Sekiban.Core.Query.MultiProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput> :
    INextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TMultiProjectionPayloadCommon : IMultiProjectionPayloadCommon
    where TQuery : ITenantNextMultiProjectionQueryAsync<TMultiProjectionPayloadCommon, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
