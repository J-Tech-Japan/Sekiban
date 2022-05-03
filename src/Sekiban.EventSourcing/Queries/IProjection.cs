namespace Sekiban.EventSourcing.Queries;

public interface IProjection
{
    Guid LastEventId { get; }
    DateTime LastTimestamp { get; }
    int Version { get; }
}
