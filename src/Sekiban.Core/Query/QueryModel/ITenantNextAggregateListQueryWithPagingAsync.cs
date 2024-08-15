using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQueryWithPagingAsync<TAggregatePayload, TOutput> :
    INextAggregateListQueryWithPagingAsync<TAggregatePayload, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
