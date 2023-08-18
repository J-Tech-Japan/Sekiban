using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single Projection Payload Interface.
///     Developer can implement this interface for Single Projection Payload.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TSingleProjectionPayload"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface
    ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> : ISingleProjectionPayloadGeneratable<TSingleProjectionPayload>,
        ISingleProjectionEventApplicable<TSingleProjectionPayload> where TAggregatePayload : IAggregatePayloadCommonBase
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon;
