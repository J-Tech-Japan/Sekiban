using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateListQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TOutput>, ITenantQueryCommon where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;