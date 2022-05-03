namespace Sekiban.EventSourcing.Queries;

public interface IProjection
{
    Guid LastEventId { get; }
    string LastSortableUniqueId { get; }
    int Version { get; }
}
