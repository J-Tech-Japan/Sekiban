using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandHandler<TAggregate, TCommand>
    where TAggregate : IAggregatePayload, new() where TCommand : ICommand<TAggregate>
{
}
