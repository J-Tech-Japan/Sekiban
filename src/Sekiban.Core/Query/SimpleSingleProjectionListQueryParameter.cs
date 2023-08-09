using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;
/// <summary>
/// Generic Query parameter for the single projection.
/// </summary>
/// <param name="PageSize"></param>
/// <param name="PageNumber"></param>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
public record SimpleSingleProjectionListQueryParameter<TSingleProjectionPayload>
    (int? PageSize, int? PageNumber) : IListQueryPagingParameter<QuerySingleProjectionState<TSingleProjectionPayload>>
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
