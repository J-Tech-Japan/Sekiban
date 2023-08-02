using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public interface
    IDeletableSingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> :
        ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload>, IDeletable where TAggregatePayload : IAggregatePayloadCommon
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
