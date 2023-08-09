using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Deletable Single Projection Payload Interface.
///     Aggregate developer can implement this interface for Deletable Single Projection Payload.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TSingleProjectionPayload">Implementation class itself</typeparam>
public interface
    IDeletableSingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> :
        ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload>, IDeletable where TAggregatePayload : IAggregatePayloadCommon
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
