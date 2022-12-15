using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjection
{
    public bool EventShouldBeApplied(IEvent ev);
    void ApplyEvent(IEvent ev);
    public bool CanApplyEvent(IEvent ev);
}
