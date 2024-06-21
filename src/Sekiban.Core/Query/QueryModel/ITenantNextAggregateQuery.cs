using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface ITenantNextAggregateQuery<TAggregatePayload, TOutput> : INextAggregateQuery<TAggregatePayload, TOutput>, ITenantQueryCommon
    where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
