using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

// ReSharper disable once UnusedTypeParameter
public interface ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> : ISingleProjectionEventApplicable<TSingleProjectionPayload>
    where TAggregatePayload : IAggregatePayloadCommon where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new();
