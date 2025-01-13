using ResultBoxes;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Projectors;

public record MultiProjectionState<TMultiProjector>(
    TMultiProjector Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version,
    string RootPartitionKey) where TMultiProjector : IMultiProjector<TMultiProjector>
{
    public MultiProjectionState() : this(
        TMultiProjector.GenerateInitialPayload(),
        Guid.Empty,
        string.Empty,
        0,
        0,
        string.Empty)
    {
    }
    public string GetPayloadVersionIdentifier() => Payload.GetVersion();
    public ResultBox<MultiProjectionState<TMultiProjector>> ApplyEvent(IEvent ev) =>
        Payload
            .Project(Payload, ev)
            .Remap(
                p => this with
                {
                    Payload = p,
                    LastEventId = ev.Id,
                    LastSortableUniqueId = ev.SortableUniqueId,
                    Version = Version + 1
                });
}
