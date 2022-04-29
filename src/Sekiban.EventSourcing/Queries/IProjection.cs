namespace Sekiban.EventSourcing.Queries;

public interface IProjection
{
    Guid LastEventId { get; }
    int Version { get; }
}
