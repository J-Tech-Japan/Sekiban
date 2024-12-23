namespace Sekiban.Core.Query;

/// <summary>
///     Basic Projection Interface.
/// </summary>
public interface IProjection
{
    Guid LastEventId { get; }
    string LastSortableUniqueId { get; }
    public int AppliedSnapshotVersion { get; }
    int Version { get; }
    string RootPartitionKey { get; }
}
