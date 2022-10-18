using Sekiban.Core.Event;
namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregateProjection
{
    void ApplyEvent(IAggregateEvent ev);
    public bool CanApplyEvent(IAggregateEvent ev);
}
