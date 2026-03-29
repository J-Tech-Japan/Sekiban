namespace Sekiban.Dcb.Storage;

/// <summary>
///     Marker interface for the primary writable event store before hybrid read decoration is applied.
/// </summary>
public interface IHotEventStore : IEventStore
{
}
