using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateQueryCommon<TAggregatePayload, TOutput> where TOutput : notnull
    where TAggregatePayload : IAggregatePayloadCommon;
