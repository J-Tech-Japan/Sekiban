using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query;

/// <summary>
///     Query response for the aggregate state.
/// </summary>
/// <param name="AggregateState"></param>
/// <typeparam name="TAggregatePayload"></typeparam>
public record QueryAggregateState<TAggregatePayload>(AggregateState<TAggregatePayload> AggregateState) : IQueryResponse
    where TAggregatePayload : IAggregatePayloadCommon;
