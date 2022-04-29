using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandResponse<T>
    where T : IAggregate
{
    public T Aggregate { get; init; }

    public AggregateCommandResponse(T aggregate) =>
        Aggregate = aggregate;
}
