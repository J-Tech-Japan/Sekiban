using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ITenantNextAggregateQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryAsync<TAggregatePayload, TOutput>,
    ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
