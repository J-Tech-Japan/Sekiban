using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextQueryCommon<TOutput>, INextQueryAsyncGeneral, ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;