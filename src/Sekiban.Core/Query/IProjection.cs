namespace Sekiban.Core.Query;

public interface IProjection
{
    Guid LastEventId { get; }
    string LastSortableUniqueId { get; }
    public int AppliedSnapshotVersion { get; }
    int Version { get; }
}
