using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQuery<TAggregatePayload, TQuery, TOutput> :
    INextAggregateListQuery<TAggregatePayload, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : ITenantNextAggregateListQuery<TAggregatePayload, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
