using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public record SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload>
    (int? PageSize, int? PageNumber) : IListQueryPagingParameter<QuerySingleProjectionState<TSingleProjectionPayload>>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
