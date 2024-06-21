using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateListQuery<TAggregatePayload, TOutput> : INextAggregateListQuery<TAggregatePayload, TOutput>, ITenantQueryCommon
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
