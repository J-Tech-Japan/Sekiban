using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query;

public record QueryAggregateState<TAggregatePayload>(AggregateState<TAggregatePayload> AggregateState) : IQueryResponse
    where TAggregatePayload : IAggregatePayloadCommon;
