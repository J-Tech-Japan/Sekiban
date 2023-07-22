using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Query;

public record SimpleAggregateListQueryParameter<TAggregatePayload>
    (int? PageSize, int? PageNumber) : IListQueryPagingParameter<QueryAggregateState<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayloadCommon;
