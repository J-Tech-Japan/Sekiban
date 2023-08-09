using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single projection interface.
/// </summary>
public interface ISingleProjection
{
    public string GetPayloadVersionIdentifier();
    public bool EventShouldBeApplied(IEvent ev);
    void ApplyEvent(IEvent ev);
}
