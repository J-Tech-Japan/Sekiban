using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateListQueryAsync<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TOutput>, INextQueryAsyncGeneral,
    ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;