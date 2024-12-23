using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query;

/// <summary>
///     Query response type for the single projection state.
/// </summary>
/// <param name="State"></param>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
public record QuerySingleProjectionState<TSingleProjectionPayload>(
    SingleProjectionState<TSingleProjectionPayload> State)
    : IQueryResponse where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
