using ResultBoxes;

namespace Sekiban.Pure.Aggregates;

public interface IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate);
}