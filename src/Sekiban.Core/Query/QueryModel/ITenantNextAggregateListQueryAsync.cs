using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateListQueryAsync<TAggregatePayload, TOutput> :
    INextAggregateListQueryAsync<TAggregatePayload, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
