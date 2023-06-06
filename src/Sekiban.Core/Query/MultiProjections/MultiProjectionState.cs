using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public record MultiProjectionState<TProjectionPayload>(
    TProjectionPayload Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version) : IProjection where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    public MultiProjectionState() : this(new TProjectionPayload(), Guid.Empty, string.Empty, 0, 0) { }
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public MultiProjectionState<TProjectionPayload> ApplyEvent(IEvent ev) =>
        this with
        {
            Payload = ((IMultiProjectionPayload<TProjectionPayload>)Payload).ApplyIEvent(ev),
            LastEventId = ev.Id,
            LastSortableUniqueId = ev.SortableUniqueId,
            Version = Version + 1
        };
}
