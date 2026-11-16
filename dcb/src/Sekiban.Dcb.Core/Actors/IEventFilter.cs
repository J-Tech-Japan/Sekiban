using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Filter for events
/// </summary>
public interface IEventFilter
{
    /// <summary>
    ///     Check if an event should be included
    /// </summary>
    bool ShouldInclude(Event evt);

    /// <summary>
    ///     SerializableEvent-first filter path. The default implementation preserves
    ///     compatibility by deserializing only when a filter does not override this method.
    /// </summary>
    bool ShouldInclude(SerializableEvent evt, DcbDomainTypes domainTypes)
    {
        // An event that cannot be deserialized is excluded; an exception from the filter itself propagates.
        Event domainEvent;
        try
        {
            domainEvent = evt.ToEvent(domainTypes.EventTypes);
        }
        catch (Exception)
        {
            return false;
        }

        return ShouldInclude(domainEvent);
    }
}
