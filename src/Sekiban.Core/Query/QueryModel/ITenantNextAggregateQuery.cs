using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TOutput>, ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;