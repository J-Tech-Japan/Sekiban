using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjection
{
    void ApplyEvent(IAggregateEvent ev);
    public bool CanApplyEvent(IAggregateEvent ev);
}
