using Sekiban.Core.Event;
namespace Sekiban.Core.Aggregate;

public record AggregateBasicInfo
{
    public bool IsDeleted { get; set; } = false;
    public Guid AggregateId { get; set; } = Guid.Empty;
    public Guid LastEventId { get; set; } = Guid.Empty;
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int AppliedSnapshotVersion { get; set; } = 0;
    public int Version { get; set; } = 0;
    public List<IAggregateEvent> Events { get; set; } = new();
}
