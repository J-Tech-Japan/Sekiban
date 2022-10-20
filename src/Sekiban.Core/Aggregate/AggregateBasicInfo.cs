using Sekiban.Core.Event;
namespace Sekiban.Core.Aggregate;

public record AggregateBasicInfo
{
    public bool IsDeleted { get; init; } = false;
    public Guid AggregateId { get; init; } = Guid.Empty;
    public Guid LastEventId { get; init; } = Guid.Empty;
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int AppliedSnapshotVersion { get; init; } = 0;
    public int Version { get; init; } = 0;
    public List<IAggregateEvent> Events { get; init; } = new();
}
