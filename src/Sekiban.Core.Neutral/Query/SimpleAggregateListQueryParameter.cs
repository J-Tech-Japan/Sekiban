using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query;

/// <summary>
///     generic query parameter for the aggregate state.
/// </summary>
/// <param name="PageSize"></param>
/// <param name="PageNumber"></param>
/// <typeparam name="TAggregatePayload"></typeparam>
public record SimpleAggregateListQueryParameter<TAggregatePayload>(int? PageSize, int? PageNumber)
    : IListQueryPagingParameter<QueryAggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayloadCommon;
