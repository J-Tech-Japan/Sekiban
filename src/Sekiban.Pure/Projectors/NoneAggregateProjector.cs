using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Projectors;

public class NoneAggregateProjector : IAggregateProjector
{
    public static NoneAggregateProjector Empty => new();
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => payload;
}
