using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQueryWithPaging<TAggregatePayload, TOutput> :
    INextAggregateListQueryWithPaging<TAggregatePayload, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    string IQueryPartitionKeyCommon.GetRootPartitionKey() => GetTenantId();
}