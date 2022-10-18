using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface IChangeAggregateCommandHandler<T, C> : IAggregateCommandHandler where T : IAggregate where C : ChangeAggregateCommandBase<T>
{
    public Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate);
    public Task<AggregateCommandResponse> HandleForOnlyPublishingCommandAsync(AggregateCommandDocument<C> aggregateCommandDocument, Guid aggregateId);
    C CleanupCommandIfNeeded(C command);
}
