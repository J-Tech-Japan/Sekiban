using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateQueryCommon<TAggregatePayload, TOutput> where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon;
public interface INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput> where TOutput : notnull
    where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon;
