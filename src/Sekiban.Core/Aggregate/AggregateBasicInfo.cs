using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Sekiban.Core.Aggregate;

public record AggregateBasicInfo
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public Guid LastEventId { get; init; } = Guid.Empty;
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int AppliedSnapshotVersion { get; init; } = 0;
    public int Version { get; init; } = 0;
}
