using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjection
{
    void ApplyEvent(IEvent ev);
    public bool CanApplyEvent(IEvent ev);
}
