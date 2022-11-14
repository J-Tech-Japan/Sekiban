using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public abstract record
    DeletableSingleProjectionPayloadBase<TAggregatePayload, TSingleProjectionPayload>() :
        SingleProjectionPayloadBase<TAggregatePayload, TSingleProjectionPayload>, IDeletable
    where TAggregatePayload : IAggregatePayload
    where TSingleProjectionPayload : ISingleProjectionPayload, new()
{
    public bool IsDeleted { get; init; }
}
