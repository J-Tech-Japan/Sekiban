using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput> :
    INextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : ITenantNextAggregateListQueryAsync<TAggregatePayload, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
