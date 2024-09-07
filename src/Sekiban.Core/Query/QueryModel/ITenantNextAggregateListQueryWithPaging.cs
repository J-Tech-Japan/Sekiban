using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQueryWithPaging<TAggregatePayload, TQuery, TOutput> :
    INextAggregateListQueryWithPaging<TAggregatePayload, TQuery, TOutput>,
    ITenantQueryCommon where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon
    where TQuery : ITenantNextAggregateListQueryWithPaging<TAggregatePayload, TQuery, TOutput>
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}
