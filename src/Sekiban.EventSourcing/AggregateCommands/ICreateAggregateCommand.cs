using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.AggregateCommands;

public interface ICreateAggregateCommand<T> : IAggregateCommand
    where T : IAggregate
{ }
