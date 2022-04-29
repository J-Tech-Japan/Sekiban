using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.AggregateCommands;

public interface ICreateAggregateCommandHandler<T, C>
    where T : IAggregate
    where C : ICreateAggregateCommand<T>
{
    internal Task<AggregateCommandResponse<T>> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument);
}
