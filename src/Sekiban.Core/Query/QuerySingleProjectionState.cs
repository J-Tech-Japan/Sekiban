using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

public record QuerySingleProjectionState<TSingleProjectionPayload>(SingleProjectionState<TSingleProjectionPayload> State) : IQueryResponse
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
